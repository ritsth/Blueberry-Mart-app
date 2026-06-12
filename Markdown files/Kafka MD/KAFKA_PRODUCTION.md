# Kafka in Production — Confluent Cloud + Cloud Run worker

> **Status: LIVE.** Confluent Cloud is the broker in prod; `blueberrymart-api` produces and
> `blueberrymart-worker` (`min-instances=1`, `RunConsumers=true`) runs the consumers. The Kafka
> secrets are in Secret Manager (`kafka-bootstrap`, `kafka-api-key`, `kafka-api-secret`). This
> doc is both the original provisioning runbook **and** the current-state reference.

Originally a runbook for taking the local-only inventory event pipeline (`KAFKA_LOCAL.md`) live.
It now carries **two** event streams: the original `inventory.stock-changed`, and `sales.events`
(order placed / payment status / review / order status) that feeds the event-sourced `sales_fact`
warehouse — see
`Markdown files/Main/SALES_EVENT_PIPELINE.md`.

## Target architecture

```
API service (blueberrymart-api, scales to zero)         ──produces──┐
   Kafka producer ON, RunConsumers OFF                              │
   + writes sales events to the transactional outbox                ▼
                                                          Confluent Cloud (Basic)
                                                          topics: inventory.stock-changed
                                                                  sales.events
                                                                    │
Worker service (blueberrymart-worker, min-instances=1)  ──consumes─┘
   RunConsumers ON →  StockEventConsumer (back-in-stock notifications)
                      BigQueryStockSink   (→ BigQuery stock_events)
                      OutboxDispatcher    (outbox_messages → sales.events)
                      BigQuerySalesSink   (sales.events → sales_* raw tables)
```

- **API** publishes stock events on order/restock/adjust and stages sales events into the
  `outbox_messages` table, but does **not** consume.
- **Worker** is the *same image*, deployed separately with `Kafka__RunConsumers=true` and
  `--min-instances=1 --no-cpu-throttling` (a Kafka consumer is a long-running loop with no
  HTTP requests, so it needs CPU always allocated). The `OutboxDispatcher` also runs here so a
  single instance publishes outbox rows without racing.

## How the code decides (already implemented)

- `Kafka:BootstrapServers` set ⇒ real producer (else no-op). Same as before.
- `Kafka:ApiKey` set ⇒ the client uses **SASL_SSL** (`KafkaConfigExtensions.WithSecurity`).
  Empty ⇒ local PLAINTEXT (Redpanda). So local dev is unchanged.
- `Kafka:RunConsumers` controls whether *this* process runs the consumers. Default: **true
  locally** (no API key), **false on the prod API** (has API key). The worker sets it true.

---

## 1. Confluent Cloud

1. Create a Confluent Cloud account (or provision via **GCP Marketplace** to bill through GCP).
2. Create a **Basic** cluster in **`us-central1`** (same region as Cloud Run/Cloud SQL).
3. Create the topics **`inventory.stock-changed`** (key `branch:item`) and **`sales.events`**
   (key = order id). Start with 6 partitions each. Confluent Cloud disallows client-side
   auto-create, so the topics must be created via Console / CLI / admin API before use.
4. Create an **API key/secret** scoped to the cluster — these are the SASL username/password.
5. Note the cluster's **bootstrap server** (e.g. `pkc-xxxxx.us-central1.gcp.confluent.cloud:9092`).

## 2. Store secrets (run yourself — values never in git)

```bash
echo -n 'pkc-xxxxx.us-central1.gcp.confluent.cloud:9092' | gcloud secrets create kafka-bootstrap --data-file=-
echo -n '<CONFLUENT_API_KEY>'    | gcloud secrets create kafka-api-key    --data-file=-
echo -n '<CONFLUENT_API_SECRET>' | gcloud secrets create kafka-api-secret --data-file=-
```

## 3. BigQuery access for the worker

The sink streams into `blueberrymart.stock_events`. Grant the Cloud Run runtime service
account (the default compute SA, unless you make a dedicated one) BigQuery write + query:

```bash
SA=278293545480-compute@developer.gserviceaccount.com
gcloud projects add-iam-policy-binding project-76ca6efe-7878-4dc8-bff \
  --member="serviceAccount:$SA" --role="roles/bigquery.dataEditor" --condition=None
gcloud projects add-iam-policy-binding project-76ca6efe-7878-4dc8-bff \
  --member="serviceAccount:$SA" --role="roles/bigquery.jobUser" --condition=None
```

(The dataset `blueberrymart` + table `stock_events` already exist from local runs; recreate
with `bq` if missing — see `KAFKA_LOCAL.md`.)

## 4. Deploy the worker

Same image as the API, but consumer-on and always-warm. It also needs the DB + JWT config
(it runs the web host for Cloud Run's health check and uses the DB for notifications):

```bash
gcloud run deploy blueberrymart-worker \
  --image us-central1-docker.pkg.dev/project-76ca6efe-7878-4dc8-bff/blueberrymart/api:latest \
  --region us-central1 --project project-76ca6efe-7878-4dc8-bff \
  --min-instances 1 --max-instances 1 --no-cpu-throttling \
  --no-allow-unauthenticated --ingress internal \
  --add-cloudsql-instances project-76ca6efe-7878-4dc8-bff:us-central1:blueberrymart-db \
  --update-env-vars "ASPNETCORE_ENVIRONMENT=Production,Kafka__RunConsumers=true,BigQuery__ProjectId=project-76ca6efe-7878-4dc8-bff" \
  --update-secrets "Jwt__Secret=jwt-secret:latest,ConnectionStrings__DefaultConnection=db-connection-string:latest,Kafka__BootstrapServers=kafka-bootstrap:latest,Kafka__ApiKey=kafka-api-key:latest,Kafka__ApiSecret=kafka-api-secret:latest"
```

> `--no-cpu-throttling` (CPU always allocated) is essential — without it the consumer loop
> is throttled between (non-existent) requests and stalls. `--min/--max-instances 1` keeps a
> single consumer per group (no rebalancing churn). `--ingress internal` keeps it private.

## 5. Turn on the producer in the API

```bash
gcloud run services update blueberrymart-api --region us-central1 \
  --update-secrets "Kafka__BootstrapServers=kafka-bootstrap:latest,Kafka__ApiKey=kafka-api-key:latest,Kafka__ApiSecret=kafka-api-secret:latest"
```

The API now publishes (RunConsumers stays false because an API key is set). The Kafka secrets
on the API persist across CI deploys (the deploy step doesn't touch them).

**CI tracks the worker (done):** `deploy.yml` has an "Update Kafka worker image" step that
redeploys `blueberrymart-worker` on the same freshly-built image as the API. It only updates the
**image** — the worker's env/secrets/scaling are set out-of-band (this section), so a manual
`--min-instances 0` (scaled down between demos) is preserved. If the worker is ever deleted,
re-provision it with the full step 4 command before relying on CI.

## 6. Verify

1. Place an order (or restock/adjust) → an event appears in the Confluent topic (Console → Topics).
2. Subscribe to an out-of-stock item, then restock it → a back-in-stock **notification** is created.
3. A row lands in BigQuery: `SELECT count(*) FROM blueberrymart.stock_events`.

## Cost & operations

- **Confluent Basic**: no hourly base fee; usage at this volume is pennies. New-account
  credits cover the early months. (Verify current pricing — it changes.)
- **Worker**: an always-on `min-instances=1` Cloud Run instance with CPU always allocated,
  ~**$12–18/month**. This is the main cost — the broker is cheap.
- **Scale to zero between demos** to cut cost: `gcloud run services update blueberrymart-worker
  --min-instances 0`. Kafka's durable log means the consumer backfills from its committed
  offset when you scale back to 1. (Set back before demoing.)
- The worker is the ops surface: if it's down, events queue durably in Kafka and process when
  it returns. Add consumer-lag monitoring for polish.

## Reliability

- **Stock events** (`inventory.stock-changed`) are still fire-and-forget *after* the DB commit,
  so a crash between commit and publish can lose one. Acceptable: they only drive back-in-stock
  notifications and the `stock_events` analytics log.
- **Sales events** (`sales.events`) use a **transactional outbox** (implemented): the event is
  written to `outbox_messages` in the *same* DB transaction as the order/payment/review, and the
  worker's `OutboxDispatcher` relays it to Kafka and stamps `published_at`. So a sales event can
  never be lost or orphaned. See `Markdown files/Main/SALES_EVENT_PIPELINE.md`.
