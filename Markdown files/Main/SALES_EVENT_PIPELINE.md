# Event-sourced `sales_fact` — the real-time sales pipeline

How the shareholder **Explore** warehouse (`blueberrymart.sales_fact`) is fed. This **replaces**
the hourly Cloud SQL federation rebuild (`analytics/sales_fact_transform.sql`) with a fully
event-driven pipeline over the existing Confluent Cloud + worker infra (see
`Markdown files/Kafka MD/KAFKA_PRODUCTION.md`). Companion doc: `BIGQUERY_ANALYTICS.md`.

## Why

The federation rebuild was up to ~1h stale. We already had Kafka live in prod, and only two
`sales_fact` fields change after an order is placed (`payment_status`, `rating`/`has_review`),
so streaming domain events + computing current state in a view makes Explore near-instant
without the batch.

## Shape

```
API (order / payment / review)                         Worker (RunConsumers=true)
  └─ write event to outbox_messages ──┐                  ┌── OutboxDispatcher ──► Kafka: sales.events
     (SAME db transaction)            │   commit         │                          │
                                      ▼                  │                          ▼
                                 Postgres ───────────────┘                   BigQuerySalesSink
                                                                                    │ append-only
                                                              ┌─────────────────────┼─────────────────────┐
                                                              ▼                     ▼                      ▼
                                                      sales_order_lines     sales_payment_status     sales_reviews
                                                              └──────────── sales_fact (VIEW) ◄───────────┘
                                                                         (latest payment + review per order)
```

## Events (topic `sales.events`, keyed by order id)

Envelope `{ type, data }` (`Models/Events/SalesEvents.cs`):

| Type | Emitted from | Carries |
|------|--------------|---------|
| `order_placed` | `OrdersController.PlaceOrder` | order + immutable line facts (item, qty, unit price, branch, member flag, order discount/delivery, per-line `rn`) |
| `payment_status_changed` | `PaymentsController` (initiate/success/failure), `ManageOrdersController.RecordPayment` | order id, status (`initiated`/`completed`/`failed`) |
| `review_changed` | `ReviewsController.SubmitReview`, `AdminController.DeleteReview` | order id, item id, rating (null = deleted) |

## Transactional outbox (guaranteed delivery)

- `OutboxMessage` → table `outbox_messages` (`id, topic, key, payload, created_at, published_at,
  attempts`); migration `AddOutboxMessages`.
- `ISalesEventOutbox` / `SalesEventOutbox` (scoped — shares the request's `DbContext`) stages the
  row; it commits atomically with the order/payment/review. No event can be lost or orphaned.
- `OutboxDispatcher` (worker-only, single instance) drains unpublished rows → Kafka, stamps
  `published_at`. Multi-instance would need `SELECT … FOR UPDATE SKIP LOCKED`.

## Sink + warehouse

- `BigQuerySalesSink` (worker-only) consumes `sales.events` and **appends** to three raw tables
  (`analytics/sales_fact_raw_tables.sql`): `sales_order_lines`, `sales_payment_status`,
  `sales_reviews`. Append-only by design — BigQuery blocks UPDATE/DELETE on freshly-streamed
  rows, so we never mutate; we recompute.
- `sales_fact` is a **VIEW** (`analytics/sales_fact_view.sql`) = order lines ⨝ latest payment per
  order ⨝ latest review per (order,item), emitting the **identical 26 columns** the old table
  had. Explore is unchanged: `BigQueryAnalyticsQueryService` still introspects
  `INFORMATION_SCHEMA.COLUMNS` and `SELECT`s by name — both work on a view.

## Correctness / safety net

The hourly schedule is **off**. The federated rebuild lives on as
`analytics/sales_fact_backfill.sql` (TRUNCATE + INSERT into the raw tables) — run **once** at
cutover to seed history, and **on demand** afterward as the reconcile/repair path (event sourcing
has no auto-heal). Parity note: cancelled/expired orders stay included, matching the old transform
(order status isn't a `sales_fact` column).

## Cutover (one-time, ordered)

1. **Deploy** the new code first (worker gets `OutboxDispatcher` + `BigQuerySalesSink`).
2. Create the `sales.events` topic in Confluent (auto-create is disabled on Confluent Cloud).
3. `bq query < analytics/sales_fact_raw_tables.sql` — create the raw tables.
4. `bq query < analytics/sales_fact_backfill.sql` — seed history from Postgres.
5. **Drop the old `sales_fact` table**, then `bq query < analytics/sales_fact_view.sql` (a view
   can't replace a table of the same name).
6. **Pause** the BigQuery scheduled query "sales_fact hourly refresh".

Steps 2–3 are additive/safe and can run before the deploy. Steps 5–6 are the destructive cutover
and must follow the deploy, or Explore would go stale while the running worker has no sales sink.

## Local / test

- Tests: `Tests/.../SalesEventOutboxTests.cs` asserts placement/review stage the right outbox row.
- Local e2e: `docker compose up redpanda`; run the API with `Kafka:RunConsumers=true` +
  `BigQuery:ProjectId`; create the raw tables + view in a scratch dataset; place an order → it
  appears via the view within seconds; complete a payment / add a review → `payment_status` /
  `rating` update on the next query.
