-- ============================================================
-- Migration: 01_InitSchema
-- Database:  BlueberryMart (PostgreSQL)
-- ============================================================

BEGIN;

-- ------------------------------------------------------------
-- ENUM TYPES
-- ------------------------------------------------------------
CREATE TYPE user_role     AS ENUM ('customer', 'shareholder');
CREATE TYPE order_type    AS ENUM ('pickup', 'delivery');
CREATE TYPE order_status  AS ENUM ('pending', 'confirmed', 'processing', 'ready', 'completed', 'cancelled');

-- ------------------------------------------------------------
-- TABLE: users
-- ------------------------------------------------------------
CREATE TABLE users (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    email          VARCHAR(255) NOT NULL,
    password_hash  TEXT         NOT NULL,
    role           user_role    NOT NULL DEFAULT 'customer',
    loyalty_points INTEGER      NOT NULL DEFAULT 0,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT pk_users         PRIMARY KEY (id),
    CONSTRAINT uq_users_email   UNIQUE (email),
    CONSTRAINT chk_loyalty_pts  CHECK (loyalty_points >= 0)
);

-- ------------------------------------------------------------
-- TABLE: branches
-- ------------------------------------------------------------
CREATE TABLE branches (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    name           VARCHAR(150) NOT NULL,
    location_city  VARCHAR(100) NOT NULL,
    is_active      BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    CONSTRAINT pk_branches PRIMARY KEY (id)
);

-- ------------------------------------------------------------
-- TABLE: inventory
-- ------------------------------------------------------------
CREATE TABLE inventory (
    id              UUID           NOT NULL DEFAULT gen_random_uuid(),
    branch_id       UUID           NOT NULL,
    item_name       VARCHAR(200)   NOT NULL,
    stock_quantity  INTEGER        NOT NULL DEFAULT 0,
    price           NUMERIC(12, 2) NOT NULL,
    is_bulk_only    BOOLEAN        NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ    NOT NULL DEFAULT NOW(),

    CONSTRAINT pk_inventory             PRIMARY KEY (id),
    CONSTRAINT fk_inventory_branch      FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT chk_stock_non_negative   CHECK (stock_quantity >= 0),
    CONSTRAINT chk_price_positive       CHECK (price > 0)
);

-- ------------------------------------------------------------
-- TABLE: orders
-- ------------------------------------------------------------
CREATE TABLE orders (
    id            UUID           NOT NULL DEFAULT gen_random_uuid(),
    user_id       UUID           NOT NULL,
    branch_id     UUID           NOT NULL,
    order_type    order_type     NOT NULL,
    status        order_status   NOT NULL DEFAULT 'pending',
    total_amount  NUMERIC(12, 2) NOT NULL,
    created_at    TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ    NOT NULL DEFAULT NOW(),

    CONSTRAINT pk_orders              PRIMARY KEY (id),
    CONSTRAINT fk_orders_user         FOREIGN KEY (user_id)   REFERENCES users    (id) ON DELETE RESTRICT,
    CONSTRAINT fk_orders_branch       FOREIGN KEY (branch_id) REFERENCES branches (id) ON DELETE RESTRICT,
    CONSTRAINT chk_total_non_negative CHECK (total_amount >= 0)
);

-- ------------------------------------------------------------
-- INDEXES
-- ------------------------------------------------------------

-- inventory: primary lookup pattern — items available at a specific branch
CREATE INDEX idx_inventory_branch_id         ON inventory (branch_id);
CREATE INDEX idx_inventory_branch_item_name  ON inventory (branch_id, item_name);
CREATE INDEX idx_inventory_bulk_only         ON inventory (branch_id, is_bulk_only) WHERE is_bulk_only = TRUE;

-- orders: frequent filters by user, branch, and status
CREATE INDEX idx_orders_user_id    ON orders (user_id);
CREATE INDEX idx_orders_branch_id  ON orders (branch_id);
CREATE INDEX idx_orders_status     ON orders (status);
CREATE INDEX idx_orders_created_at ON orders (created_at DESC);

-- users: case-insensitive email lookup
CREATE INDEX idx_users_email_lower ON users (lower(email));

COMMIT;
