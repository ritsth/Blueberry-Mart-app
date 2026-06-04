-- Demo analytics data: backdated orders spread over the last 14 days so the
-- shareholder charts have something to show. These are marked status='completed'
-- (real app orders start as 'pending'), which makes them easy to remove:
--
--   DELETE FROM order_items WHERE order_id IN (SELECT id FROM orders WHERE status = 'completed');
--   DELETE FROM orders WHERE status = 'completed';
--
DO $$
DECLARE
  day_offset   int;
  orders_today int;
  j            int;
  v_item       record;
  v_user       uuid;
  v_qty        int;
  v_total      numeric;
  v_order      uuid;
  v_otype      text;
  v_created    timestamptz;
BEGIN
  FOR day_offset IN 1..14 LOOP
    orders_today := 2 + floor(random() * 4)::int;   -- 2–5 orders per day
    FOR j IN 1..orders_today LOOP
      SELECT id INTO v_user
        FROM users WHERE role = 'customer' ORDER BY random() LIMIT 1;

      SELECT id, branch_id, price INTO v_item
        FROM inventory
        WHERE stock_quantity > 0 AND NOT is_bulk_only
        ORDER BY random() LIMIT 1;

      v_qty     := 1 + floor(random() * 4)::int;     -- 1–4 units
      v_total   := v_item.price * v_qty;
      v_otype   := CASE WHEN random() < 0.3 THEN 'delivery' ELSE 'pickup' END;
      v_created := NOW()
                   - (day_offset || ' days')::interval
                   - (floor(random() * 10) || ' hours')::interval;
      v_order   := gen_random_uuid();

      INSERT INTO orders
        (id, user_id, branch_id, order_type, status, total_amount,
         discount_amount, delivery_fee, created_at, updated_at)
      VALUES
        (v_order, v_user, v_item.branch_id, v_otype, 'completed', v_total,
         0, CASE WHEN v_otype = 'delivery' THEN 100 ELSE 0 END, v_created, v_created);

      INSERT INTO order_items (id, order_id, item_id, quantity, unit_price)
      VALUES (gen_random_uuid(), v_order, v_item.id, v_qty, v_item.price);
    END LOOP;
  END LOOP;
END $$;
