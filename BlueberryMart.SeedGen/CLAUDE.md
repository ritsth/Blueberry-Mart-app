# BlueberryMart.SeedGen — synthetic analytics data generator

One-shot console tool that fabricates ~3 years of realistic order history (growth trend,
Dashain/Tihar + year-end seasonality, weekend lift, member vs guest behavior, skewed reviews) and
load-jobs it into BigQuery.

`dotnet run --project BlueberryMart.SeedGen -- --rows 200000 --seed 42`

## ⚠️ LEGACY / INCOMPATIBLE with the current warehouse
`sales_fact` is now a **VIEW** over append-only raw tables (event-sourced pipeline, see
`analytics/CLAUDE.md` + `Markdown files/Main/SALES_EVENT_PIPELINE.md`). This tool still
**drops/creates `sales_fact` as a TABLE**, which collides with that view.

- Do **not** run it against the live `blueberrymart` dataset.
- Point `--table` at a throwaway table, or retire the tool.
- To seed/reconcile the real warehouse, use `analytics/sales_fact_backfill.sql` instead.

The ~195k synthetic rows it produced are preserved in `blueberrymart.sales_fact_synthetic`.

## Notes
- Reads the live catalog from Postgres (local `blueberry-postgres` container) to keep item
  names/prices realistic; derives `category` from item names (same keyword logic as the app/view).
