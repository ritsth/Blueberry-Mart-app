-- Migration: 02_AddReviews

BEGIN;

CREATE TABLE reviews (
    id          UUID        NOT NULL DEFAULT gen_random_uuid(),
    user_id     UUID        NOT NULL,
    order_id    UUID        NOT NULL,
    item_id     UUID        NOT NULL,
    rating      SMALLINT    NOT NULL,
    comment     TEXT        NOT NULL,
    image_path  TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT pk_reviews               PRIMARY KEY (id),
    CONSTRAINT fk_reviews_user          FOREIGN KEY (user_id)  REFERENCES users     (id) ON DELETE RESTRICT,
    CONSTRAINT fk_reviews_order         FOREIGN KEY (order_id) REFERENCES orders    (id) ON DELETE RESTRICT,
    CONSTRAINT fk_reviews_item          FOREIGN KEY (item_id)  REFERENCES inventory (id) ON DELETE RESTRICT,
    CONSTRAINT chk_rating_range         CHECK (rating BETWEEN 1 AND 5)
);

CREATE INDEX idx_reviews_item_id  ON reviews (item_id);
CREATE INDEX idx_reviews_order_id ON reviews (order_id);

COMMIT;
