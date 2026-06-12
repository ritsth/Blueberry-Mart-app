-- Backfill + on-demand reconcile for the event-sourced sales warehouse.
--
-- Rebuilds the three append-only raw tables from the LIVE prod Postgres (Cloud SQL) via the
-- federated EXTERNAL_QUERY connection, then the `sales_fact` VIEW reflects them. Run it:
--   * ONCE at cutover, to seed history before turning the hourly schedule off; and
--   * ON DEMAND afterwards, as the manual repair path if events are ever lost/mis-mapped
--     (event sourcing has no automatic self-heal — this is the safety net).
--
-- Idempotent: TRUNCATE + INSERT, so re-running fully rebuilds from Postgres truth. Requires the
-- raw tables to exist first (analytics/sales_fact_raw_tables.sql).
--   bq query --use_legacy_sql=false "$(cat analytics/sales_fact_backfill.sql)"
--
-- NOTE: replaces the *scheduled* sales_fact_transform.sql (which is kept only for reference).

TRUNCATE TABLE `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_order_lines`;
INSERT INTO `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_order_lines`
  (order_id, order_line_id, order_number, occurred_at, branch_name, item_id, item_name,
   is_bulk, order_type, is_member, customer_id, quantity, unit_price, order_discount,
   order_delivery_fee, rn)
SELECT * FROM EXTERNAL_QUERY("project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us", '''
  SELECT
    o.id::text          AS order_id,
    oi.id::text         AS order_line_id,
    o.order_number      AS order_number,
    o.created_at        AS occurred_at,
    b.name              AS branch_name,
    inv.id::text        AS item_id,
    inv.item_name       AS item_name,
    inv.is_bulk_only    AS is_bulk,
    o.order_type::text  AS order_type,
    (u.member_since IS NOT NULL AND o.created_at >= u.member_since
       AND (u.member_until IS NULL OR o.created_at <= u.member_until)) AS is_member,
    o.user_id::text     AS customer_id,
    oi.quantity         AS quantity,
    oi.unit_price       AS unit_price,
    o.discount_amount   AS order_discount,
    o.delivery_fee      AS order_delivery_fee,
    row_number() OVER (PARTITION BY o.id ORDER BY oi.id) AS rn
  FROM order_items oi
  JOIN orders o      ON o.id = oi.order_id
  JOIN inventory inv ON inv.id = oi.item_id
  JOIN branches b    ON b.id = o.branch_id
  JOIN users u       ON u.id = o.user_id
''');

TRUNCATE TABLE `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_payment_status`;
INSERT INTO `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_payment_status`
  (order_id, payment_status, occurred_at)
SELECT * FROM EXTERNAL_QUERY("project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us", '''
  SELECT p.order_id::text AS order_id, p.status::text AS payment_status, p.updated_at AS occurred_at
  FROM payments p
''');

TRUNCATE TABLE `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_reviews`;
INSERT INTO `project-76ca6efe-7878-4dc8-bff.blueberrymart.sales_reviews`
  (order_id, item_id, rating, occurred_at)
SELECT * FROM EXTERNAL_QUERY("project-76ca6efe-7878-4dc8-bff.us.bbm-cloudsql-us", '''
  SELECT r.order_id::text AS order_id, r.item_id::text AS item_id, r.rating AS rating, r.created_at AS occurred_at
  FROM reviews r
''');
