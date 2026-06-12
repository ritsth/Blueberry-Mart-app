# analytics/ — BigQuery SQL for the `sales_fact` warehouse

SQL that defines the shareholder **Explore** warehouse in BigQuery
(`project-76ca6efe-7878-4dc8-bff.blueberrymart`). Full design:
`Markdown files/Main/SALES_EVENT_PIPELINE.md` + `Markdown files/Main/BIGQUERY_ANALYTICS.md`.

`sales_fact` is now a **VIEW** fed by the Kafka sales-event pipeline (cutover 2026-06-12); the
hourly federation rebuild is retired (schedule paused) and repurposed as the reconcile tool.

| File | What it is | When to run |
|------|-----------|-------------|
| `sales_fact_raw_tables.sql` | DDL for the 3 append-only raw tables (`sales_order_lines`, `sales_payment_status`, `sales_reviews`). **Schema source of truth** — must match `BigQuerySalesSink`'s insert-row keys. | once per dataset |
| `sales_fact_view.sql` | `CREATE OR REPLACE VIEW sales_fact` over the raw tables (latest payment per order, latest review per order+item). Emits the **identical 26 columns** the old table had. | after raw tables exist |
| `sales_fact_backfill.sql` | TRUNCATE + INSERT the raw tables from prod Postgres via `EXTERNAL_QUERY`. Seeds history **and** is the **on-demand reconcile/repair tool** (no auto-heal in event sourcing). | cutover + on demand |
| `sales_fact_transform.sql` | **SUPERSEDED.** The old hourly `CREATE OR REPLACE TABLE` federation rebuild, kept for reference only. | never (disabled) |

## Conventions / gotchas
- Run with `bq query --use_legacy_sql=false --project_id=… < file.sql` (feed via **stdin** — the
  leading `--` comments confuse `bq`'s arg parser if passed inline).
- The 26 `sales_fact` columns/types/order must stay identical (the Explore catalog introspects
  `INFORMATION_SCHEMA.COLUMNS`; `BigQueryAnalyticsQueryService` has hardcoded measure-aggs/labels
  keyed by column name). If you change the view's columns, keep parity or update that service.
- `category` is derived by a regex `CASE` on `item_name` (inventory has no category column) —
  mirror the same keyword list used in the app + `SeedGen`.
- A view can't replace a table of the same name: drop the old `sales_fact` table before creating
  the view.
- Federation connection: `project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us` (US-location to match
  the US dataset). Paused scheduled-query transfer config:
  `…/locations/us/transferConfigs/6a3a1a5c-0000-241c-9309-f4f5e80ac3bc`.
