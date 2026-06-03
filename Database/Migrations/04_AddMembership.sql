-- Membership flag on users
ALTER TABLE users
  ADD COLUMN IF NOT EXISTS is_member    BOOLEAN     NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS member_since TIMESTAMPTZ;

-- Discount applied to an order (e.g. member 5% off)
ALTER TABLE orders
  ADD COLUMN IF NOT EXISTS discount_amount NUMERIC(12,2) NOT NULL DEFAULT 0;
