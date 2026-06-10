# Kafka in Production — Confluent Cloud + Cloud Run worker

Runbook for taking the local-only inventory event pipeline (`KAFKA.md`) live, using
**Confluent Cloud** as the managed broker and a **dedicated Cloud Run worker** for the
consumers. The application code already supports this — no code changes are needed to
provision it.

## Target architecture

```
API service (blueberrymart-api, scales to zero)         ──produces──┐
   Kafka producer ON, RunConsumers OFF                              ▼
                                                          Confluent Cloud (Basic)
                                                          topic: inventory.stock-changed
                                                                    │
Worker service (blueberrymart-worker, min-instances=1)  ──consumes─┘
   RunConsumers ON →  StockEventConsumer (back-in-stock notifications)
                      BigQueryStockSink   (→ BigQuery stock_events)
```

- **API** publishes events on order/restock/adjust but does **not** consume.
- **Worker** is the *same image*, deployed separately with `Kafka__RunConsumers=true` and
  `--min-instances=1 --no-cpu-throttling` (a Kafka consumer is a long-running loop with no
  HTTP requests, so it needs CPU always allocated).

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
3. Create the topic **`inventory.stock-changed`** (start with 6 partitions; key is `branch:item`).
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
with `bq` if missing — see `KAFKA.md`.)

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

The API now publishes (RunConsumers stays false because an API key is set). To bake this into
CI, add the same three `--update-secrets` to the deploy step in `.github/workflows/deploy.yml`.

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

## Reliability (optional, later)

Producing is fire-and-forget *after* the DB commit, so a crash between commit and publish
loses that event. For exactly-once, add a **transactional outbox** (write the event to an
`outbox` table in the same DB transaction; a relay publishes it). Out of scope for the demo.
