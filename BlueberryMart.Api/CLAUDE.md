# BlueberryMart.Api — .NET 8 Web API

The backend. Root `CLAUDE.md` has the stack, commands, layered-folder architecture, and DB
conventions — this file adds the non-obvious bits specific to this project.

## Event pipelines (Kafka + BigQuery)

Two independent streams run over Confluent Cloud (LIVE in prod) + a Cloud Run **worker**:

- **`inventory.stock-changed`** — emitted on order/restock/adjust (`IStockEventProducer`,
  fire-and-forget after commit). Consumers: `StockEventConsumer` (back-in-stock notifications),
  `BigQueryStockSink` (→ `blueberrymart.stock_events`).
- **`sales.events`** — order-placed / payment-status / review / order-status events behind the
  event-sourced `sales_fact` warehouse. Emitted via a **transactional outbox** (`ISalesEventOutbox`
  → `outbox_messages`, same DB txn as the change). `OutboxDispatcher` publishes them;
  `BigQuerySalesSink` appends to four raw tables. `order_status_changed` (confirm/complete/cancel/
  expire) drives the `order_status` dimension; **revenue = paid AND not cancelled**. See
  `Markdown files/Main/SALES_EVENT_PIPELINE.md`.

### Producer vs consumer split (important)
- `Kafka:BootstrapServers` empty ⇒ Kafka disabled (no-op producer, no consumers) — tests + any
  no-config run.
- `Kafka:RunConsumers` gates ALL hosted consumers (`StockEventConsumer`, `OutboxDispatcher`,
  `BigQueryStockSink`, `BigQuerySalesSink`, `OrderExpirySweeper`). **Prod API = produces only;
  the always-on `blueberrymart-worker` (same image, `RunConsumers=true`, min-instances=1) = the
  only consumer host.** So background loops/sweepers run once, on the worker.
- `Kafka:ApiKey` set ⇒ SASL_SSL (`KafkaConfigExtensions.WithSecurity`); empty ⇒ local PLAINTEXT.

## Gotchas
- New hosted consumers must be gated on `runConsumers` in `Program.cs` (and `bigQueryConfigured`
  for BQ sinks), or they'll try to run on the prod API/in tests.
- Outbox events are added to the request's `DbContext` (don't `SaveChanges` in the outbox writer —
  the caller's transaction persists them atomically).
- `Order.OrderNumber` is DB-generated (sequence) — only populated after `SaveChanges`. The
  `OrderPlaced` event is built *after* the first save, before commit.
- CI runs `dotnet format BlueberryMart.Api --verify-no-changes` as a hard gate before tests.
  Run `dotnet format BlueberryMart.Api` before pushing.
- Migrations apply automatically on startup (`DbInitializer`). Add via
  `dotnet dotnet-ef migrations add <Name> --project BlueberryMart.Api --output-dir Migrations`.
