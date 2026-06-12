# BigQuery Analytics — how the Explore tab's `sales_fact` gets its data

Reference for the data behind the shareholder **Explore** tab. The analytics *engine*
(catalog + query + saved reports) is in `CUSTOM_ANALYTICS_PLAN.md`; this doc is about
**where the numbers come from**.

---

## Architecture change — event-sourced `sales_fact` (Option C)

**The pipeline is moving from the hourly federation batch (Option A) to a fully event-driven
design.** The application code has landed; the one-time prod cutover (below) is the remaining
manual step.

- Confluent Cloud **is** live in prod (the API produces; `blueberrymart-worker`, `minScale=1`,
  `RunConsumers=true`, runs the consumers). The old "prod has zero Kafka" note was stale.
- `sales_fact` becomes a **VIEW** (`analytics/sales_fact_view.sql`) over three append-only raw
  tables — `sales_order_lines`, `sales_payment_status`, `sales_reviews` — streamed into by
  `BigQuerySalesSink` from the `sales.events` Kafka topic. Events are emitted via a
  **transactional outbox** (`outbox_messages` + `OutboxDispatcher`) at order placement, payment
  status changes, and review submit/delete. Reads (Explore catalog + queries) are unchanged
  because the view emits the identical 26 columns.
- **Cutover steps (run once):** pre-create the `sales.events` topic in Confluent; run
  `analytics/sales_fact_raw_tables.sql`; **drop the old `sales_fact` table** then run
  `analytics/sales_fact_view.sql`; run `analytics/sales_fact_backfill.sql` to seed history;
  **pause** the hourly scheduled query.
- **Safety net:** the hourly schedule is off, but the federated rebuild lives on in
  `analytics/sales_fact_backfill.sql` as an **on-demand** reconcile/repair tool (event sourcing
  has no auto-heal).

Everything below describes the previous (Option A) batch design, retained for history.

## What was running before (Option A, LIVE until cutover)

Prod `sales_fact` is rebuilt from the **live prod Postgres** by a **BigQuery scheduled
query with Cloud SQL federation** — Option A below. No Kafka, no app code, no always-on
worker.

- **Federation connection:** `project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us`
  (BigQuery → Cloud SQL `blueberrymart-db`). A `US`-location connection so it matches the
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
   `.github/workflows/deploy.yml` `--update-env-vars`; the Cloud Run runtime SA
   `278293545480-compute@developer.gserviceaccount.com` granted `roles/bigquery.jobUser` +
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
