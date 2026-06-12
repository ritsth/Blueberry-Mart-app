-- SUPERSEDED (kept for reference). `sales_fact` is now a VIEW fed by the sales event
-- pipeline (analytics/sales_fact_view.sql), and the hourly schedule is OFF. The federated
-- rebuild now lives in analytics/sales_fact_backfill.sql (it rebuilds the append-only raw
-- tables, not this single table) and is run on demand only, as the reconcile/repair path.
--
-- Rebuilds the BigQuery `sales_fact` warehouse table from the LIVE prod Postgres
-- (Cloud SQL) via a federated EXTERNAL_QUERY. This is the body of the hourly
-- BigQuery scheduled query "sales_fact hourly refresh (Postgres federation)".
--
-- Runs as the service account bbm-analytics-etl@<project>.iam.gserviceaccount.com
-- (roles: bigquery.jobUser, bigquery.dataEditor, bigquery.connectionUser).
-- Federation connection: project.us.bbm-cloudsql-us  ->  Cloud SQL blueberrymart-db.
--
-- Grain: one row per order-line. Order-level money (discount, delivery fee) is placed
-- only on the primary line (rn = 1) so SUM doesn't double-count. `category` is derived
-- from item_name (inventory has no category column). is_member is evaluated against the
-- buyer's membership window at order time.
--
-- To run manually:  bq query --use_legacy_sql=false "$(cat analytics/sales_fact_transform.sql)"

CREATE OR REPLACE TABLE `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_fact` AS
SELECT * FROM EXTERNAL_QUERY("project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us", '''
WITH joined AS (
  SELECT
    o.id AS order_id, oi.id AS order_line_id, o.order_number,
    o.created_at AS occurred_at,
    b.name AS branch_name, inv.item_name, inv.is_bulk_only AS is_bulk,
    o.order_type::text AS order_type,
    (u.member_since IS NOT NULL AND o.created_at >= u.member_since
       AND (u.member_until IS NULL OR o.created_at <= u.member_until)) AS is_member,
    COALESCE(p.status::text, 'none') AS payment_status,
    o.user_id AS customer_id,
    oi.quantity, oi.unit_price,
    (oi.quantity * oi.unit_price) AS line_revenue,
    o.discount_amount AS order_discount, o.delivery_fee AS order_delivery_fee,
    r.rating,
    row_number() OVER (PARTITION BY o.id ORDER BY oi.id) AS rn
  FROM order_items oi
  JOIN orders o      ON o.id = oi.order_id
  JOIN inventory inv ON inv.id = oi.item_id
  JOIN branches b    ON b.id = o.branch_id
  JOIN users u       ON u.id = o.user_id
  LEFT JOIN payments p ON p.order_id = o.id
  LEFT JOIN reviews  r ON r.order_id = o.id AND r.item_id = oi.item_id
)
SELECT
  order_id::text, order_line_id::text, order_number,
  occurred_at, occurred_at::date AS order_date,
  EXTRACT(YEAR FROM occurred_at)::int  AS year,
  EXTRACT(MONTH FROM occurred_at)::int AS month,
  to_char(occurred_at, 'YYYY-MM')      AS year_month,
  trim(to_char(occurred_at, 'Day'))    AS day_of_week,
  EXTRACT(HOUR FROM occurred_at)::int  AS hour,
  branch_name,
  CASE
    WHEN lower(item_name) ~ 'spinach|tomato|lettuce|veg|fruit|apple|banana' THEN 'Produce'
    WHEN lower(item_name) ~ 'bread|sourdough|bun|bagel|pastry' THEN 'Bakery'
    WHEN lower(item_name) ~ 'milk|yogurt|cheese|egg|butter|cream' THEN 'Dairy & Eggs'
    WHEN lower(item_name) ~ 'chicken|meat|fish|beef|pork|mutton' THEN 'Meat & Poultry'
    WHEN lower(item_name) ~ 'rice|lentil|flour|wheat|bean|pulse|grain' THEN 'Grains & Pulses'
    WHEN lower(item_name) ~ 'oil' THEN 'Cooking Oil'
    WHEN lower(item_name) ~ 'juice|water|soda|drink|beverage' THEN 'Beverages'
    WHEN lower(item_name) ~ 'sugar|salt|spice' THEN 'Pantry'
    ELSE 'Other'
  END AS category,
  item_name, order_type, is_member, is_bulk, payment_status,
  customer_id::text, quantity, unit_price, line_revenue,
  (CASE WHEN rn = 1 THEN order_discount     ELSE 0 END)::numeric AS discount_amount,
  (CASE WHEN rn = 1 THEN order_delivery_fee ELSE 0 END)::numeric AS delivery_fee,
  rating, (rating IS NOT NULL) AS has_review, (rn = 1) AS is_order_primary_line
FROM joined
''')
