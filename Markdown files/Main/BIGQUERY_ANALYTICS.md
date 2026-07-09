# BigQuery Analytics — how the Explore tab's `sales_fact` gets its data

Reference for the data behind the shareholder **Explore** tab. The analytics *engine*
(catalog + query + saved reports) is in `CUSTOM_ANALYTICS_PLAN.md`; this doc is about
**where the numbers come from**.

---

## What is running now (LIVE) — event-sourced `sales_fact` (Option C)

**Cutover DONE 2026-06-12.** `sales_fact` is no longer a table rebuilt hourly; it is a **VIEW**
fed by a Kafka event pipeline. Full design: `Markdown files/Main/SALES_EVENT_PIPELINE.md`.

- Confluent Cloud **is** live in prod (the API produces; `blueberrymart-worker`, `minScale=1`,
  `RunConsumers=true`, runs the consumers). The old "prod has zero Kafka" note was stale.
- `sales_fact` is a **VIEW** (`analytics/sales_fact_view.sql`) over four append-only raw tables —
  `sales_order_lines`, `sales_payment_status`, `sales_reviews`, `sales_order_status` — streamed
  into by `BigQuerySalesSink` from the `sales.events` Kafka topic. Events are emitted via a
  **transactional outbox** (`outbox_messages` + `OutboxDispatcher`) at order placement, payment
  status changes, review submit/delete, and order status changes. The catalog introspects
  `INFORMATION_SCHEMA.COLUMNS`, so new columns appear automatically.

### Update 2026-06-11 — `order_status` dimension + collected-revenue rule

Cancellations now reach analytics. Added an `order_status_changed` event (emitted on
confirm/complete/cancel/expire), a `sales_order_status` raw table, and an **`order_status`** column
on the view (27 columns; defaults to `pending`). The agreed rule is **revenue = money actually
collected = `payment_status='completed'` AND `order_status!='cancelled'`** — a paid order cancelled
later is a refund (`cancelled` ∩ `completed`): kept in the warehouse for analysis, excluded from
revenue. Both surfaces apply it: the Postgres home dashboard (`ShareholderController.GetAnalytics`)
and the Explore "Collected revenue only" toggle. See `SALES_EVENT_PIPELINE.md`.

### Update 2026-06-12 — `channel` dimension (online vs in-store)

In-store till sales were added. Every `OrderPlaced` event now carries `Channel` (`online` |
`in_store`), streamed into a new `channel` column on `sales_order_lines` and surfaced on the view as
`COALESCE(l.channel, 'online') AS channel` (28 columns; pre-channel rows read as `online`). Explore
auto-introspects it as a group-by dimension — no service change.

**Prod apply — DONE 2026-06-13:** ran `ALTER TABLE sales_order_lines ADD COLUMN IF NOT EXISTS
channel STRING` (a re-run of `sales_fact_raw_tables.sql` won't add a column to an existing table),
then re-applied `sales_fact_view.sql`. Verified: all 754 historical rows read `channel='online'`;
in-store sales land as `in_store` once the worker streams them.

**Cutover DONE 2026-06-11** (commits `384a474`, `7e382b5`): created `sales_order_status`; seeded
**313** status rows from prod `orders` (completed 178 / cancelled 64 / processing 25 / confirmed 24
/ ready 22); `CREATE OR REPLACE VIEW` added `order_status` (754 lines unchanged). Verified collected
revenue = **836,265** vs unfiltered **1,014,480** (cancelled orders had no completed payments, so
they only drop unpaid/pending money). The 3 live raw tables were left untouched (targeted seed, not
a full backfill).

### What we actually ran at cutover (2026-06-12)

1. **Deployed** the code (commits `25fa861`, `702370e`) → worker picked up `OutboxDispatcher` +
   `BigQuerySalesSink`.
2. **Created the `sales.events` topic** (6 partitions, RF=3) on Confluent via a one-off
   `Confluent.Kafka` AdminClient using the Secret Manager creds (no Kafka CLI installed).
3. `bq query < analytics/sales_fact_raw_tables.sql` — created the 3 raw tables.
4. `bq query < analytics/sales_fact_backfill.sql` — seeded from prod Postgres:
   **754 order lines / 254 payment rows / 2 reviews**.
5. **Dropped** the old `sales_fact` TABLE, then `bq query < analytics/sales_fact_view.sql` — a
   view can't replace a table of the same name. Parity verified: **754 rows, 309 orders,
   revenue 1,014,480, 614 paid lines, 2 reviews**.
6. **Paused** the hourly scheduled query (`bq update --transfer_config --no_auto_scheduling
   projects/<PROJECT_NUMBER>/locations/us/transferConfigs/6a3a1a5c-0000-241c-9309-f4f5e80ac3bc`
   — see `GCP_SERVICES.md` for the project number) — schedule now empty, no next run.
7. Verified the live worker logged `BigQuerySalesSink streaming sales.events -> …` and the
   transient "table not found" 404s (it started before step 3) stopped — the sink's retry guard
   kept the worker up.

- **Safety net:** the hourly schedule is OFF, but the federated rebuild lives on as
  `analytics/sales_fact_backfill.sql` (TRUNCATE + INSERT the raw tables) — run **on demand** to
  reconcile/repair, since event sourcing has no auto-heal. The synthetic ~195k demo rows remain
  in `blueberrymart.sales_fact_synthetic`.
- **Papercut:** the `kafka-bootstrap` secret value has a trailing `/ ` (slash+space); librdkafka
  tolerates it, but worth cleaning up.

Everything below describes the previous (Option A) batch design, **now retired** as the live feed
and kept for history (its rebuild SQL is repurposed as the reconcile tool above).

## What ran before (Option A — federated batch, RETIRED 2026-06-12)

Prod `sales_fact` is rebuilt from the **live prod Postgres** by a **BigQuery scheduled
query with Cloud SQL federation** — Option A below. No Kafka, no app code, no always-on
worker.

- **Federation connection:** `<PROJECT_ID>.us.bbm-cloudsql-us` (see `GCP_SERVICES.md` for the
  project ID) (BigQuery → Cloud SQL `blueberrymart-db`). A `US`-location connection so it matches the
  `blueberrymart` dataset (which is US multi-region); it still reaches the us-central1 instance.
- **Transform:** `analytics/sales_fact_transform.sql` — `CREATE OR REPLACE TABLE sales_fact AS
  SELECT … EXTERNAL_QUERY(…)` joining `orders`/`order_items`/`inventory`/`branches`/`users`/
  `payments`/`reviews` into the wide order-line fact rows.
- **Schedule:** BigQuery scheduled query **"sales_fact hourly refresh (Postgres federation)"**,
  runs **every 1 hour**, as service account `bbm-analytics-etl@…` (roles: `bigquery.jobUser`,
  `bigquery.dataEditor`, `bigquery.connectionUser`; the BQ Data Transfer service agent has
  `tokenCreator` on it).
- So: place a real order in prod → within the hour it shows up in Explore.

**Note — prod data is currently tiny.** Prod's order history was wiped earlier, so `sales_fact`
holds only a handful of real rows and the charts are sparse until real orders accumulate. The
real orders are dated *now* (2026), so in the app use the **"All"** time range (or the current
year) to see them — the default 2025 filter shows nothing.

**Synthetic demo data is preserved** in `blueberrymart.sales_fact_synthetic` (~195k rows). To
flip Explore back to the rich demo: `bq cp -f blueberrymart.sales_fact_synthetic
blueberrymart.sales_fact` (and pause the scheduled query, or it'll overwrite again next hour).

---

## Option A — Batch / federation ETL (BUILT, in use)

The setup above. Properties:
- No app code, no broker, no always-on worker — just a BigQuery connection + a scheduled query.
- Analytics is up to ~1 hour stale (the refresh interval). Fine for OLAP.
- Full rebuild each run (cheap at this data size; would go incremental if it ever got large).

One-time setup that was done (for reference / disaster recovery):
0. **API side (so prod reads BigQuery at all):** `BigQuery__ProjectId` set on Cloud Run via
   `.github/workflows/deploy.yml` `--update-env-vars`; the Cloud Run runtime SA (default compute
   SA — see `GCP_SERVICES.md`) granted `roles/bigquery.jobUser` +
   `roles/bigquery.dataViewer`. Without this the endpoints report `enabled:false`.
1. `bq mk --connection --connection_type=CLOUD_SQL … --location=US bbm-cloudsql-us`
   (credentials pulled from the `db-connection-string` secret).
2. Granted the connection's service agent `roles/cloudsql.client`.
3. Created SA `bbm-analytics-etl` + granted `jobUser` / `dataEditor` / `connectionUser`.
4. Enabled `bigquerydatatransfer`; granted its service agent `tokenCreator` on the ETL SA.
5. `bq mk --transfer_config --data_source=scheduled_query --schedule="every 1 hours"
   --service_account_name=bbm-analytics-etl@… --params='{"query": <analytics/sales_fact_transform.sql>}'`.

---

## Option B — Kafka streaming (NOT built; possible future upgrade)

Real-time instead of hourly. By far the biggest lift; only worth it for the streaming-architecture
learning, not for getting numbers on a screen. Concrete checklist:

1. **A managed Kafka broker.** Prod has zero Kafka (only the local Redpanda in `docker-compose`).
   Confluent Cloud / Redpanda Cloud / GCP Managed Kafka. New paid service + credentials.
2. **SASL/TLS in the code.** Current `ProducerConfig`/`ConsumerConfig` are plaintext. Managed Kafka
   needs `SecurityProtocol=SaslSsl` + secrets. Code change.
3. **An always-on consumer host.** Cloud Run scales to zero; a consumer must run 24/7 (separate
   worker with `--min-instances=1`, GKE, or a VM) — new infra that costs money idle.
4. **A whole new event + sink.** The existing pipeline emits `stock-changed` events, not order-line
   facts. You'd define a new order-line event, emit it from `OrdersController`, and write a new
   BigQuery sink mapping it into `sales_fact`. Essentially a brand-new pipeline.
5. **A transactional outbox** so events aren't lost between DB commit and publish.
6. **A one-time backfill** — Kafka only carries new orders.

If pursued, it would *replace* the hourly scheduled query as the feed into `sales_fact`. See `KAFKA_LOCAL.md`.
