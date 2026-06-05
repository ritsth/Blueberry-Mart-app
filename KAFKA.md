# Inventory Event Pipeline (Kafka) — Status & Plan

A staged project to learn Kafka by building real-time inventory features on top of
an event stream. End goal: item **availability per branch**, **"notify me when back
in stock"** (Sephora-style), and **analytics in BigQuery** — all fed by one stream
of stock-change events.

**Status:** ✅ **Stages 1–3 working locally** — events flow, drive back-in-stock
notifications, and stream to BigQuery for analytics. Production roll-out (managed
Kafka) is the remaining step.

---

## Why Kafka here (the mental model)

Three different jobs, three different tools — Kafka is the pipe between them:

| Job | Example | Tool |
|---|---|---|
| Operational read | "Is X in stock at branch Y *now*?" | **Postgres** (Cloud SQL) |
| Event backbone | "stock changed", "order placed" | **Kafka** |
| Analytics / history | stock-out frequency, sales trends | **BigQuery** |

The payoff is **fan-out**: one event stream, many consumers with different purposes.

```
API (producer)
  └── inventory.stock-changed ──> Kafka
                                    │
              ┌─────────────────────┼──────────────────────┐
              ▼                     ▼                        ▼
       availability /        back-in-stock            BigQuery sink
       read model            notifications            (analytics)
       (Postgres)            (Sephora feature)        (shareholder dashboards)
```

> Note: in this app the **availability lookup is just a Postgres query** — the
> `inventory` table already holds stock per branch. Kafka earns its keep on the
> *event-driven* features (notifications, analytics), not the read itself.

---

## Stage 1 — stock-change events (DONE)

Every order placement publishes a `StockChanged` event.

**Files**
- `docker-compose.yml` — local **Redpanda** broker + **Console** UI.
- `BlueberryMart.Api/Models/Events/StockChangedEvent.cs` — the event payload.
- `BlueberryMart.Api/Configuration/KafkaOptions.cs` — bound from the `"Kafka"` section.
- `BlueberryMart.Api/Services/Interfaces/IStockEventProducer.cs`
- `BlueberryMart.Api/Services/KafkaStockEventProducer.cs` — real Confluent.Kafka producer.
- `BlueberryMart.Api/Services/NoOpStockEventProducer.cs` — used when Kafka is off.
- `BlueberryMart.Api/Controllers/OrdersController.cs` — publishes after the order commits.
- `BlueberryMart.Api/Program.cs` — picks the real vs no-op producer from config.

**Event schema** (`inventory.stock-changed`, key = `{branchId}:{itemId}`):
```json
{
  "ItemId": "…", "BranchId": "…", "ItemName": "Brown Eggs (12 pack)",
  "OldQuantity": 35, "NewQuantity": 33,
  "Reason": "order_placed", "OccurredAt": "2026-06-05T07:58:51Z"
}
```

**Opt-in design (important):** Kafka is enabled only when `Kafka:BootstrapServers`
is set.
- **Local dev:** `appsettings.Development.json` sets `localhost:19092` → real producer.
- **Production & tests:** no `Kafka:BootstrapServers` → **no-op producer**, so nothing
  depends on a broker. Production stays a no-op until/unless we add managed Kafka.

Producing is **fire-and-forget** and wrapped so it can never break order placement.
(Not yet transactionally guaranteed — a future "transactional outbox" would make
delivery exactly-once with the DB write.)

---

## Stage 2 — back-in-stock notifications (DONE)

A **consumer** turns the event stream into the Sephora-style "notify me when it's
back" feature.

**Flow:** shareholder restocks → `stock-changed` event (`reason: "restock"`) →
consumer sees the `0 → positive` transition → creates a notification for every
subscriber.

**Files**
- `Models/Entities/StockSubscription.cs`, `Models/Entities/Notification.cs` (+ migration `AddBackInStock`).
- `Controllers/InventoryController.cs` — `POST /{id}/restock` (shareholder, emits event)
  and `POST /{id}/notify-me` (customer subscribes to an out-of-stock item).
- `Controllers/NotificationsController.cs` — `GET /api/notifications`, `POST /api/notifications/read`.
- `Services/StockEventConsumer.cs` — `BackgroundService` consumer (group
  `blueberrymart-backinstock`, `EnableAutoCommit=false` → commits manually after
  processing = at-least-once).
- `Program.cs` — registers the consumer **only when Kafka is configured**.

**Concepts:** consumer, **consumer group**, `AutoOffsetReset.Earliest`, **manual
offset commit** (at-least-once delivery), a singleton `BackgroundService` using a
scoped `DbContext` per message.

> **Production note:** the endpoints work in prod, but because prod has the **no-op
> producer** (no broker), restocks emit nothing and no back-in-stock notifications
> fire there yet. The feature goes live in prod once managed Kafka is added.

## Stage 3 — BigQuery analytics warehouse (DONE)

A **second consumer group** streams the same events into BigQuery — Kafka fan-out:
the back-in-stock consumer and this sink read the stream independently.

**Files / infra**
- BigQuery dataset `blueberrymart`, table `stock_events` (created via `bq`).
- `Configuration/BigQueryOptions.cs` — bound from the `"BigQuery"` section (opt-in via `ProjectId`).
- `Services/BigQueryStockSink.cs` — `BackgroundService` consumer (group
  `blueberrymart-bigquery-sink`) that streaming-inserts each event into the table.
- `Services/BigQueryInventoryAnalytics.cs` (+ `DisabledInventoryAnalytics`) — queries BQ.
- `Controllers/ShareholderController.cs` — `GET /api/shareholders/inventory-analytics`
  (reports `enabled:false` when BigQuery isn't configured).
- `Program.cs` — registers the analytics service always (real vs disabled) and the
  sink only when **both** Kafka and BigQuery are configured.

**Concepts:** **fan-out** (multiple consumer groups, one topic), a streaming **sink**
to a warehouse, and OLAP **analytics** (`GROUP BY reason`) vs the OLTP Postgres reads.

> BigQuery is cloud-only: locally the app uses **Application Default Credentials**
> (`gcloud auth application-default login`) against a real BQ dataset in the project.

## Running it locally

```bash
# 1. start the broker + console
docker compose up -d
#    Kafka API:        localhost:19092
#    Redpanda Console: http://localhost:8080

# 2. run the API in Development (Kafka enabled) — needs local Postgres 'blueberry_mart'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project BlueberryMart.Api \
  --no-launch-profile --urls http://localhost:5099

# 3. place an order (via the app or curl) → an event is published

# 4. watch events
#    - browser: http://localhost:8080 → Topics → inventory.stock-changed
#    - CLI:
docker exec redpanda rpk topic consume inventory.stock-changed --offset start

# stop
kill $(lsof -ti tcp:5099)     # API
docker compose down           # broker (+ -v to wipe topic data)
```

---

## Roadmap

### Production (later, separate effort)
Production Kafka needs an always-on broker (managed: Confluent / Redpanda Cloud / GCP
Managed Kafka) **and** an always-on consumer — which Cloud Run (scales to zero) isn't
ideal for. Deferred until the local pipeline is solid.

---

## Concepts covered so far
**Topic** (the durable event log) · **partition + key** (same `branch:item` key →
same partition → ordering) · **offset** (position in the log). Stages 2–3 add
**consumers**, **consumer groups**, **offset commits**, and a **sink connector**.
