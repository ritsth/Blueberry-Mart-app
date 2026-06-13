-- Append-only raw tables that the sales event pipeline (BigQuerySalesSink) streams into, and
-- that the `sales_fact` VIEW (analytics/sales_fact_view.sql) reads. These are the schema source
-- of truth: the streaming sink's BigQueryInsertRow keys must match these column names/types.
--
-- Run once per dataset to create them (idempotent). For prod cutover and for local/test datasets.
--   bq query --use_legacy_sql=false "$(cat analytics/sales_fact_raw_tables.sql)"

-- One immutable row per order line, from OrderPlaced events.
CREATE TABLE IF NOT EXISTS `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_order_lines` (
  order_id           STRING,
  order_line_id      STRING,
  order_number       INT64,
  occurred_at        TIMESTAMP,
  branch_name        STRING,
  item_id            STRING,
  item_name          STRING,
  is_bulk            BOOL,
  order_type         STRING,
  channel            STRING,    -- sales origin: 'online' | 'in_store' (NULL for pre-channel rows → 'online' in the view)
  is_member          BOOL,
  customer_id        STRING,
  quantity           INT64,
  unit_price         NUMERIC,
  order_discount     NUMERIC,   -- order-level; the view assigns it to the primary line only
  order_delivery_fee NUMERIC,   -- order-level; ditto
  rn                 INT64      -- 1-based line position; rn=1 is the primary line
);

-- One row per payment status change (initiated | completed | failed). Latest per order wins.
CREATE TABLE IF NOT EXISTS `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_payment_status` (
  order_id       STRING,
  payment_status STRING,
  occurred_at    TIMESTAMP
);

-- One row per review submit/delete. rating NULL = deleted (tombstone). Latest per (order,item) wins.
CREATE TABLE IF NOT EXISTS `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_reviews` (
  order_id    STRING,
  item_id     STRING,
  rating      INT64,
  occurred_at TIMESTAMP
);

-- One row per order status change (confirmed | completed | cancelled). Latest per order wins;
-- the view defaults orders with no row to 'pending'. Drives the order_status dimension and the
-- revenue rule (a refund is order_status='cancelled' on a payment_status='completed' order).
CREATE TABLE IF NOT EXISTS `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_order_status` (
  order_id    STRING,
  status      STRING,
  occurred_at TIMESTAMP
);
