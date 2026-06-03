-- Migration: 03_AddOrderItems

BEGIN;

CREATE TABLE order_items (
    id          UUID           NOT NULL DEFAULT gen_random_uuid(),
    order_id    UUID           NOT NULL,
    item_id     UUID           NOT NULL,
    quantity    INTEGER        NOT NULL,
    unit_price  NUMERIC(12, 2) NOT NULL,

    CONSTRAINT pk_order_items          PRIMARY KEY (id),
    CONSTRAINT fk_order_items_order    FOREIGN KEY (order_id) REFERENCES orders    (id) ON DELETE RESTRICT,
    CONSTRAINT fk_order_items_item     FOREIGN KEY (item_id)  REFERENCES inventory (id) ON DELETE RESTRICT,
    CONSTRAINT chk_quantity_positive   CHECK (quantity > 0),
    CONSTRAINT chk_unit_price_positive CHECK (unit_price > 0)
);

CREATE INDEX idx_order_items_order_id ON order_items (order_id);
CREATE INDEX idx_order_items_item_id  ON order_items (item_id);

COMMIT;
