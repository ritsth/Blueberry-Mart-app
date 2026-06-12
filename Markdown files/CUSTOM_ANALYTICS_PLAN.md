# Self-Service Analytics ("Explore") — Plan

Let shareholders build **their own** charts on the fly — pick dimensions, metrics,
filters, and a chart type — and **save the configuration** (not the data) to re-run
against fresh data later. Backed by a **wide BigQuery fact table** seeded with ~3
years of realistic synthetic history so the warehouse is actually exercised.

**Status:** ✅ **All phases complete** (Phase 1 data → 2 query engine → 3 saved reports →
4 frontend Explore tab). Backend build/format/50 tests green; frontend `tsc` clean.
The feature is **prod-inert** (no `BigQuery:ProjectId` in prod ⇒ `enabled:false`); seeing
data requires the BigQuery-enabled dev API.

Decisions locked in:
- **Builder openness:** introspected & broad — the field catalog is generated from the
  BigQuery table schema, so most columns are selectable. Wide-open feel, **no free-form
  SQL** (the safety line we hold).
- **Scale & store:** ~200k order-line rows in **BigQuery** (`sales_fact`), realistic
  seasonality/growth/branch-skew. Re-activates the parked BQ project
  (`project-76ca6efe-7878-4dc8-bff`).
- Postgres stays the live OLTP; BigQuery is the analytical history. Saved-report
  **configs** live in Postgres.

This is opt-in exactly like the Kafka/BigQuery pipeline: no `BigQuery:ProjectId` ⇒ the
analytics endpoints report `enabled:false` and nothing touches the warehouse
(production stays untouched until BQ is deliberately configured).

---

## Phase 1 — BigQuery `sales_fact` table + seed generator

### Table: `blueberrymart.sales_fact` (one row per order-line)
Wide/denormalized so arbitrary `GROUP BY` needs no joins — the whole point of OLAP.

| Column | Type | Role | Notes |
|---|---|---|---|
| `order_id` | STRING | — | for `COUNT(DISTINCT order_id)` = order count |
| `order_line_id` | STRING | — | grain |
| `order_number` | INT64 | — | |
| `occurred_at` | TIMESTAMP | — | order time |
| `order_date` | DATE | dimension | |
| `year`,`month`,`day_of_week`,`hour` | INT64 / STRING | dimension | precomputed date grains |
| `branch_name` | STRING | dimension | |
| `category` | STRING | dimension | |
| `item_name` | STRING | dimension | |
| `order_type` | STRING | dimension | pickup / delivery |
| `is_member` | BOOL | dimension | |
| `is_bulk` | BOOL | dimension | |
| `payment_status` | STRING | dimension | completed / failed / initiated / none |
| `order_status` | STRING | dimension | pending / confirmed / processing / ready / completed / cancelled (added 2026-06-11) |
| `customer_id` | STRING | dimension | (high-cardinality; for distinct counts) |
| `quantity` | INT64 | measure | |
| `unit_price` | NUMERIC | measure | |
| `line_revenue` | NUMERIC | measure | `quantity * unit_price` |
| `discount_amount` | NUMERIC | measure | member 5% etc. |
| `delivery_fee` | NUMERIC | measure | order-level; only on primary line |
| `rating` | INT64 (nullable) | measure | review score if reviewed |
| `has_review` | BOOL | dimension | |
| `is_order_primary_line` | BOOL | — | exactly one per order; gate for order-level money |

**Grain handling (important):** money that is per-*order* (delivery_fee, order discount)
lives only on the `is_order_primary_line = true` row to avoid double-counting; the
catalog tags those measures so the builder filters on the primary-line flag
automatically. Order count is always `COUNT(DISTINCT order_id)`.

### Seed generator — `BlueberryMart.SeedGen/` (new C# console project)
- Reads real **branches + inventory** from Postgres (keeps names/categories/prices
  consistent with the live catalog), plus a pool of ~3–5k synthetic customers.
- Walks **~3 years of days**; daily order count = `base × growthTrend(t) ×
  seasonality(month) × weekdayFactor(dow) × noise`. Seasonality gives holiday spikes,
  growth gives an upward slope, weekday factor gives weekend lift.
- Per order: skewed branch pick (one flagship dominates), member vs guest (members buy
  more / get 5% off / unlock bulk), pickup vs delivery, 1–6 line items by category,
  quantities, prices → revenue/discount/fee. Mostly `completed` payments, a few
  `failed`. A fraction of lines get a review with a **high-skewed rating distribution**.
- **Reproducible** via fixed `Random(seed)`.
- Writes **newline-delimited JSON** and loads it with a **BigQuery load job** (free +
  fast) — *not* 200k streaming inserts (slow/costly). Target ~200k lines.
- Run: `dotnet run --project BlueberryMart.SeedGen -- --rows 200000 --seed 42`.

---

## Phase 2 — Introspected catalog + query endpoint (backend)

New `Controllers/AnalyticsController.cs` (`[Authorize(Roles="Shareholder")]`) +
`Services/AnalyticsQueryService.cs` (real) / `DisabledAnalyticsQueryService` (when BQ
off), registered in `Program.cs` like `IInventoryAnalytics`.

### `GET /api/analytics/catalog`
Introspects `sales_fact` via `INFORMATION_SCHEMA.COLUMNS` (cached). Classifies:
- STRING/BOOL/DATE → **dimension**; numeric → **measure** (with allowed aggs:
  `sum,avg,min,max,count,count_distinct`).
- Adds derived date-grain dimensions (year/month/week/day_of_week) over `order_date`.
- A small **overrides map** for friendly labels + to hide raw id columns / tag
  primary-line-only measures.

Returns the field metadata so the frontend renders pickers dynamically — this is what
makes it feel "open" without hand-maintaining a list.

### `POST /api/analytics/query`
Request = a **validated spec of catalog IDs**, never SQL:
```json
{
  "measures":   [{"field":"line_revenue","agg":"sum"},
                 {"field":"order_id","agg":"count_distinct"}],
  "dimensions": ["category","month"],
  "filters":    [{"field":"branch_name","op":"in","values":["Flagship"]},
                 {"field":"order_date","op":"between","values":["2024-01-01","2024-12-31"]}],
  "orderBy":    [{"field":"line_revenue_sum","dir":"desc"}],
  "limit":      1000,
  "chartType":  "bar"
}
```
**Safety (the core of "broad but not free-form"):**
- Every `field`/`agg` is resolved against the server catalog **by ID** → only vetted
  column names/expressions ever reach the SQL string. Unknown IDs → 400.
- All filter **values** become `BigQueryParameter`s (parameterized), never concatenated.
- Guardrails: **require a date-range filter**, cap dimensions (≤3), cap `limit`
  (≤5000), allowed aggs per type only, enforce primary-line gating on order-level
  measures.
- Returns `{ columns: [...], rows: [...] }` shaped for charting.

So the SQL is assembled dynamically, but **no client string is ever interpolated as
SQL** — the client only supplies catalog keys + parameter values.

---

## Phase 3 — Saved reports (Postgres)

`Models/Entities/SavedReport.cs`: `Id` (uuid), `ShareholderId` (FK users),
`Name`, `ConfigJson` (jsonb — the query spec + chartType), `CreatedAt`, `UpdatedAt`.
Wire into `BlueberryMartDbContext` (snake_case, defaults, FK Restrict); migration
`AddSavedReports`.

Endpoints (under `AnalyticsController`, scoped to the calling shareholder):
- `GET /api/analytics/reports` — list mine
- `POST /api/analytics/reports` — save `{ name, config }`
- `GET /api/analytics/reports/{id}` — one
- `PUT /api/analytics/reports/{id}` — rename / update config
- `DELETE /api/analytics/reports/{id}`

Running a saved report = client loads its `config` → calls `/query`. **Config only,
always fresh data** — exactly the design.

---

## Phase 4 — Frontend "Explore" screen

New shareholder tab (under `ShareholderTabs.tsx`), reusing the existing
`react-native-chart-kit` setup (Bar/Line/Pie) + `chartConfig` from
`ShareholderHomeTab.tsx`.
- On load: `GET /catalog` → render **dimension picker**, **measure picker** (field +
  agg), **filter builder** (incl. required date range), **chart-type** selector.
- **Run** → `POST /query` → render the chart (Bar/Line/Pie by chartType) with a table
  fallback for shapes that don't chart cleanly.
- **Save** → name prompt → `POST /reports`. A **Saved** list loads a report's config
  back into the pickers and re-runs it.
- New `src/services/analyticsService.ts` for catalog/query/reports calls.

---

## Production note
Same opt-in shape as Kafka/BigQuery: prod has no `BigQuery:ProjectId`, so the catalog/
query endpoints report `enabled:false` and the Explore tab shows an "analytics
warehouse not configured" state. Going live later needs: `BigQuery:ProjectId` env +
the Cloud Run SA granted `roles/bigquery.jobUser` (+ `dataViewer`), and a real ETL from
Postgres → `sales_fact` (today it's a one-shot synthetic seed). Out of scope now.

---

## Verification
1. **Seed:** run the generator → `bq query 'SELECT COUNT(*) FROM blueberrymart.sales_fact'`
   ≈ 200k; spot-check seasonality (`GROUP BY month`) shows a believable curve, not flat.
2. **Catalog:** `GET /api/analytics/catalog` lists dimensions + measures with aggs.
3. **Query:** a few specs (revenue by category by month; member vs guest revenue;
   avg rating by branch) return sane rows; a tampered/unknown field → 400; missing date
   filter → 400.
4. **Saved reports:** save → list → reload → re-run returns fresh data; delete works;
   another shareholder can't see mine.
5. **Frontend:** build a chart end-to-end, save it, reload it; BQ-off shows the disabled
   state.
6. `dotnet build` + `dotnet test` green; `dotnet format --verify-no-changes` clean.

---

## Build order
Phase 1 (data first — everything else needs it) → Phase 2 (catalog + query) →
Phase 3 (saved reports) → Phase 4 (frontend). Each phase is independently testable.
