# analytics/ — BigQuery SQL for the `sales_fact` warehouse

SQL that defines the shareholder **Explore** warehouse in BigQuery
(`project-76ca6efe-7878-4dc8-bff.blueberrymart`). Full design:
`Markdown files/Main/SALES_EVENT_PIPELINE.md` + `Markdown files/Main/BIGQUERY_ANALYTICS.md`.

`sales_fact` is now a **VIEW** fed by the Kafka sales-event pipeline (cutover 2026-06-12); the
hourly federation rebuild is retired (schedule paused) and repurposed as the reconcile tool.

| File | What it is | When to run |
|------|-----------|-------------|
| `sales_fact_raw_tables.sql` | DDL for the 4 append-only raw tables (`sales_order_lines`, `sales_payment_status`, `sales_reviews`, `sales_order_status`). **Schema source of truth** — must match `BigQuerySalesSink`'s insert-row keys. Idempotent (`IF NOT EXISTS`), so re-run to add a table — but **`IF NOT EXISTS` won't add a *column* to an existing table**: a new column (e.g. `channel`) needs a one-off `ALTER TABLE sales_order_lines ADD COLUMN channel STRING` on existing datasets. | once per dataset |
| `sales_fact_view.sql` | `CREATE OR REPLACE VIEW sales_fact` over the raw tables (latest payment / review / status per order). Emits the original 26 columns **plus `order_status` and `channel`** (28; both `COALESCE`d — `order_status`→`'pending'`, `channel`→`'online'`). | after raw tables exist |
| `sales_fact_backfill.sql` | TRUNCATE + INSERT the raw tables from prod Postgres via `EXTERNAL_QUERY`. Seeds history **and** is the **on-demand reconcile/repair tool** (no auto-heal in event sourcing). | cutover + on demand |
| `sales_fact_transform.sql` | **SUPERSEDED.** The old hourly `CREATE OR REPLACE TABLE` federation rebuild, kept for reference only. | never (disabled) |

## Conventions / gotchas
- Run with `bq query --use_legacy_sql=false --project_id=… < file.sql` (feed via **stdin** — the
  leading `--` comments confuse `bq`'s arg parser if passed inline).
- The catalog introspects `INFORMATION_SCHEMA.COLUMNS`: numeric columns → measures, STRING → a
  dimension automatically, so **adding** a STRING column (like `order_status`) is safe and needs no
  service change. `BigQueryAnalyticsQueryService` only has hardcoded aggs/labels keyed by the
  existing **measure** column names — don't rename/remove those without updating it.
- **Revenue = collected = `payment_status='completed'` AND `order_status!='cancelled'`** (a paid +
  cancelled order is a refund: kept for analysis, out of revenue). `order_status` defaults to
  `pending` in the view; emitted on confirm/complete/cancel/expire. Same rule in the Postgres home
  dashboard (`ShareholderController`) and the Explore "Collected revenue only" toggle.
- `category` is derived by a regex `CASE` on `item_name` (inventory has no category column) —
  mirror the same keyword list used in the app + `SeedGen`.
- A view can't replace a table of the same name: drop the old `sales_fact` table before creating
  the view.
- Federation connection: `project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us` (US-location to match
  the US dataset). Paused scheduled-query transfer config:
  `…/locations/us/transferConfigs/6a3a1a5c-0000-241c-9309-f4f5e80ac3bc`.
