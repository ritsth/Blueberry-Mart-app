# Kafka Inventory Pipeline — Concepts & Local Setup

The event-driven half of Blueberry Mart: a stream of stock-change events that powers
**back-in-stock notifications** and **BigQuery analytics**. This doc is the mental model +
how to run it **locally on Redpanda**. For taking it live see **`KAFKA_PRODUCTION.md`**
(Confluent Cloud + a Cloud Run worker).

> **Status:** Stages 1–3 work locally. The code supports a managed broker (SASL_SSL) and a
> dedicated consumer worker, but **production has Kafka off** until it's deliberately wired up.

## Why Kafka here (pick the tool by the shape of the problem)

| Question | Tool | Example |
|---|---|---|
| "Is X in stock at branch Y **right now**?" | **Postgres** (OLTP) | a fast point-in-time read |
| "**What changed?**" → react to it | **Kafka** (event log) | notify on restock, feed analytics |
| "**Trends** over history?" | **BigQuery** (OLAP) | stock-out frequency, sales over months |

The first lesson came *before* any Kafka: an availability check is just a **database read** —
the `inventory` table already holds stock per branch. Kafka earns its place on the *other*
half — reacting to **changes**. So we emit one event type:
`stock-changed { item, branch, oldQty, newQty, reason, at }`, keyed by `branch:item`.

## Core vocabulary (with our examples)

- **Topic** — a durable, append-only, **ordered log**. Ours: `inventory.stock-changed`.
  Writing appends; reading doesn't delete, so many readers share one log.
- **Producer** — writes events. Our API produces on order placement, restock, and stock adjust.
- **Consumer** — reads and acts. We have two.
- **Partition + key** — a topic splits into partitions for parallelism; same **key** → same
  partition → **ordered**. We key by `branch:item`, so you never see "back in stock" before
  the sale that emptied it.
- **Offset** — an event's position in the log. A consumer **commits** "processed up to N";
  restart → resume there.
- **Consumer group** — consumers sharing a topic's partitions and one committed position.
  **Different groups are independent** — each gets *every* event with its *own* offset.

## The two ideas that make it click

**Decoupling** — the producer doesn't know who's listening. The restock endpoint appends an
event and returns immediately; it has no idea a notification will fire or a warehouse row
will be written. Add a consumer tomorrow and the producer doesn't change.

**Fan-out (the payoff)** — two consumer groups read the same log at their own pace:

```
order / restock / adjust ──> inventory.stock-changed (the log)
                                   │
        ┌──────────────────────────┴──────────────────────────┐
   group: blueberrymart-backinstock          group: blueberrymart-bigquery-sink
   → back-in-stock notifications (Postgres)   → analytics warehouse (BigQuery)
```

One slow/broken consumer doesn't affect the other. That's Kafka's superpower: one source of
truth, many independent reactions.

## Delivery guarantees

Consumers commit the offset **only after** processing (`EnableAutoCommit=false`) →
**at-least-once** delivery: a crash mid-processing redelivers the event, so handlers are
**idempotent** (e.g. a subscription is marked "notified" so a redelivery can't double-notify).
The producer is weaker today — it publishes *after* the DB commit, so a crash in that gap
loses the event; the production fix is a **transactional outbox** (see `KAFKA_PRODUCTION.md`).

## The three stages (all working locally)

1. **Producer + topic** — events append to the log; inspect partitions/keys/offsets in the
   Redpanda Console.
2. **Consumer + group** — `StockEventConsumer` turns the stream into the back-in-stock
   feature (offset commits, at-least-once).
3. **Second group + sink** — `BigQueryStockSink` streams the same events into BigQuery; then
   analytics over the history (OLAP vs OLTP made concrete).

## File map

| File | Role |
|---|---|
| `docker-compose.yml` | local **Redpanda** broker + Console UI |
| `Models/Events/StockChangedEvent.cs` | the event payload |
| `Configuration/KafkaOptions.cs` | the `"Kafka"` config section (bootstrap, API key, `RunConsumers`) |
| `Configuration/KafkaConfigExtensions.cs` | `WithSecurity()` — SASL_SSL when an API key is set, else PLAINTEXT |
| `Services/KafkaStockEventProducer.cs` / `NoOpStockEventProducer.cs` | real vs no-op producer |
| `Services/StockEventConsumer.cs` | back-in-stock consumer (group `blueberrymart-backinstock`) |
| `Services/BigQueryStockSink.cs` | BigQuery sink (group `blueberrymart-bigquery-sink`) |
| `Controllers/InventoryController.cs` | `restock` / `notify-me`; `ManageInventoryController` `adjust` |
| `Controllers/NotificationsController.cs` | `GET /api/notifications`, mark-read |
| `Program.cs` | picks real vs no-op producer; registers consumers per `RunConsumers` |

## Opt-in design (local vs prod from one codebase)

Everything keys off config, so **the same code runs locally and in prod** — only settings differ:

- `Kafka:BootstrapServers` empty ⇒ **no-op producer**, consumers don't run (production today, and tests).
- `Kafka:ApiKey` set ⇒ client uses **SASL_SSL** (managed broker); empty ⇒ **PLAINTEXT** (local Redpanda).
- `Kafka:RunConsumers` ⇒ does *this* process consume? Defaults **true locally** (no key) and
  **false on the prod API** (which only produces); the prod **worker** sets it true.

**Why Redpanda locally, Confluent in prod?** They're the same Kafka protocol, so the code is
identical — Redpanda is a tiny, free, offline-friendly broker ideal for dev; Confluent Cloud is
a managed, durable, authenticated broker for prod. Same idea as local Postgres vs Cloud SQL.

## Running it locally

```bash
# 1. start the broker + console
docker compose up -d
#    Kafka API:        localhost:19092
#    Redpanda Console: http://localhost:8080

# 2. run the API in Development (Kafka enabled via appsettings.Development.json) — needs the
#    local Postgres 'blueberry_mart'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project BlueberryMart.Api \
  --no-launch-profile --urls http://localhost:5099

# 3. place an order / restock / adjust stock  → an event is published

# 4. watch events
#    browser: http://localhost:8080 → Topics → inventory.stock-changed
docker exec redpanda rpk topic consume inventory.stock-changed --offset start

# stop
docker compose down            # add -v to wipe topic data
```

BigQuery is cloud-only even locally: the sink uses **Application Default Credentials**
(`gcloud auth application-default login`) against a real BQ dataset (`blueberrymart.stock_events`)
in `project-76ca6efe-7878-4dc8-bff`.

## When *not* to reach for Kafka (honest trade-off)

This is **overkill** for an app this size — and that's fine, the goal was learning. You'd reach
for Kafka with multiple services needing the same events, real-time pipelines, high write
throughput, or replay needs. For a single app, an in-process event bus or an `outbox` table +
cron often does the job with a fraction of the operational weight (a broker to run, consumers
to keep alive, schemas, lag to monitor). Knowing *when it's overkill* is part of the lesson.
