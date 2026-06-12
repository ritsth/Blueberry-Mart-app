-- Defines `sales_fact` as a VIEW over the append-only raw tables fed by the sales event
-- pipeline (replaces the hourly federation rebuild in sales_fact_transform.sql). Emits the
-- SAME 26 columns/types/order as the old table, so the self-service "Explore" catalog
-- (BigQueryAnalyticsQueryService, which introspects INFORMATION_SCHEMA.COLUMNS and SELECTs
-- from this name) keeps working unchanged. Current state is computed at read time:
-- latest payment status per order, latest review per (order,item).
--
--   bq query --use_legacy_sql=false "$(cat analytics/sales_fact_view.sql)"

CREATE OR REPLACE VIEW `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_fact` AS
WITH latest_payment AS (
  SELECT order_id, payment_status FROM (
    SELECT order_id, payment_status,
           ROW_NUMBER() OVER (PARTITION BY order_id ORDER BY occurred_at DESC) AS rn
    FROM `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_payment_status`
  ) WHERE rn = 1
),
latest_review AS (
  SELECT order_id, item_id, rating FROM (
    SELECT order_id, item_id, rating,
           ROW_NUMBER() OVER (PARTITION BY order_id, item_id ORDER BY occurred_at DESC) AS rn
    FROM `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_reviews`
  ) WHERE rn = 1
)
SELECT
  l.order_id,
  l.order_line_id,
  l.order_number,
  l.occurred_at,
  DATE(l.occurred_at)                       AS order_date,
  EXTRACT(YEAR  FROM l.occurred_at)         AS year,
  EXTRACT(MONTH FROM l.occurred_at)         AS month,
  FORMAT_TIMESTAMP('%Y-%m', l.occurred_at)  AS year_month,
  FORMAT_TIMESTAMP('%A', l.occurred_at)     AS day_of_week,
  EXTRACT(HOUR FROM l.occurred_at)          AS hour,
  l.branch_name,
  CASE
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'spinach|tomato|lettuce|veg|fruit|apple|banana') THEN 'Produce'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'bread|sourdough|bun|bagel|pastry') THEN 'Bakery'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'milk|yogurt|cheese|egg|butter|cream') THEN 'Dairy & Eggs'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'chicken|meat|fish|beef|pork|mutton') THEN 'Meat & Poultry'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'rice|lentil|flour|wheat|bean|pulse|grain') THEN 'Grains & Pulses'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'oil') THEN 'Cooking Oil'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'juice|water|soda|drink|beverage') THEN 'Beverages'
    WHEN REGEXP_CONTAINS(LOWER(l.item_name), r'sugar|salt|spice') THEN 'Pantry'
    ELSE 'Other'
  END                                       AS category,
  l.item_name,
  l.order_type,
  l.is_member,
  l.is_bulk,
  COALESCE(p.payment_status, 'none')        AS payment_status,
  l.customer_id,
  l.quantity,
  l.unit_price,
  (l.quantity * l.unit_price)               AS line_revenue,
  IF(l.rn = 1, l.order_discount,     CAST(0 AS NUMERIC)) AS discount_amount,
  IF(l.rn = 1, l.order_delivery_fee, CAST(0 AS NUMERIC)) AS delivery_fee,
  r.rating,
  (r.rating IS NOT NULL)                    AS has_review,
  (l.rn = 1)                                AS is_order_primary_line
FROM `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_order_lines` l
LEFT JOIN latest_payment p ON p.order_id = l.order_id
LEFT JOIN latest_review  r ON r.order_id = l.order_id AND r.item_id = l.item_id;
