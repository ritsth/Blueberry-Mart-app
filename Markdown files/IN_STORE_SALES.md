# In-Store Sales — process & design

How a walk-in purchase at a physical Blueberry Mart counter is rung up, recorded, and reflected in
loyalty and analytics. This is the **point-of-sale (POS)** flow, distinct from the online
customer-app flow.

Related: `Main/BACK_OFFICE_PORTAL.md` (the portal), `Main/BACKEND.md` (API), `Main/SALES_EVENT_PIPELINE.md`
+ `Main/BIGQUERY_ANALYTICS.md` (analytics).

---

## 1. Why it exists

Online orders walk a fulfilment lifecycle (`pending → confirmed → processing → ready → completed`).
A counter sale is the opposite: it's **paid and handed over on the spot**. We model it as a first-class
sale so that in-store revenue, stock, and loyalty behave exactly like online — and so we can compare
**online vs in-store** in analytics — without forcing it through the online lifecycle.

Two orthogonal concepts make this work:
- **`Order.Channel`** = `online` | `in_store` — the *sales origin* (analytics dimension).
- **`Order.OrderType`** = `pickup` | `delivery` — the *fulfilment method* (in-store sales use `pickup`).

---

## 2. Who can sell, and where

- The till is for back-office roles: **staff, manager, admin** (`[Authorize(Roles="Staff,Manager,Admin")]`).
- **Staff/managers sell only at their own branch** (the `branch` claim on their JWT). **Admins** pick a
  branch in the UI and pass `branchId`.
- In the portal, **staff land directly on the till** as their home page; managers/admins reach it from
  the sidebar **Sell** link. (`BlueberryMartPortal/src/pages/SellPage.tsx`, `Dashboard.tsx`.)

---

## 3. The ring-up process (happy path)

1. **Open the till** (Sell page). The branch's **active, in-stock, retail** catalogue loads.
   - **Bulk items are excluded** — bulk is members-only wholesale, which makes no sense at an anonymous
     walk-in register. Enforced in the UI *and* the API (a bulk item → `400`).
2. **Search & add items** to the ticket (left pane → right-pane ticket). Quantities are typable and
   stepper-adjustable, clamped to available stock. The ticket shows a **running total**.
3. **Attach a customer (optional)** — see §4.
4. **Choose a payment method** — cash / card / eSewa. This is **recorded only** (`instore:<method>`);
   no payment gateway is called (the cashier collected the money in person).
5. **Complete sale** → `POST /api/orders/manage/in-store-sale`. In one transaction the backend:
   - validates the items belong to the branch and have stock, then **deducts stock**;
   - creates the `Order` already **`completed`**, `channel = in_store`, `OrderType = pickup`, with a
     **`completed` `Payment`**;
   - applies the **member discount** only if an attached customer is a member (no delivery fee in-store);
   - **credits loyalty** to an attached customer (anonymous walk-ins earn nothing);
   - emits the same sales events as any sale — `order_placed` (carrying `channel`),
     `payment_status_changed → completed`, `order_status_changed → completed` — so it lands in the
     `sales_fact` warehouse like everything else;
   - emits a `stock-changed` event per line (reason `in_store_sale`).
6. **Receipt** — a printable slip pops up (branch, order #, date, cashier, customer/Walk-in, line items,
   subtotal/discount/total, payment). **Print** uses `@media print` to show only the slip; **New sale**
   clears the ticket for the next customer. The sale also appears on the **Orders** page as
   `completed` / `in_store`.

---

## 4. Customer attribution (3 options)

| Option | How | Loyalty | `Order.UserId` |
|---|---|---|---|
| **Anonymous walk-in** | leave it blank | none | `null` |
| **Attach existing** | search by **email or phone** (`GET /api/orders/manage/customers?q=`) | credited | the customer |
| **Quick-create guest** | **+ New customer** → enter **phone** (`POST /api/orders/manage/customers`) | credited | the new guest |

- A **guest** is a real `User` with **phone only** — `Email`/`PasswordHash` are null (it can't log in
  yet). `Phone` is **unique** and is the dedup/lookup key: creating a guest with a known phone returns
  the existing record (idempotent), so repeat visits don't make duplicates.
- **Phones are digits-only, ≤10** (`Validation/PhoneNumber`), enforced on create and at sign-up.
- A member attached to the sale gets the live member-discount rate (read from `GET /api/system/status`,
  so the till preview matches the charged total).

---

## 5. Loyalty lifecycle & account claim

The point of capturing a phone is to start earning loyalty **before** the customer has an app account,
then let them keep it:

```
walk-in pays in store
   └─ staff "+ New customer" → guest (phone only)         ← earns loyalty from purchase #1
        └─ repeat visits found by phone                    ← keeps accruing
             └─ later: customer signs up in the app with that SAME phone
                  └─ CLAIM: register attaches email+password to the same row
                       └─ inherits all loyalty + order history (no new account)
```

- **Claim at sign-up:** `POST /api/auth/register` with an optional `phone`. If a guest with that
  phone exists (no email yet), register upgrades that **same row** to a full account. (App: the
  Register screen has an optional Phone field — *"link in-store purchases & loyalty"*.)
- **Claim later:** an existing account links its phone via `POST /api/profile/link-phone`, which
  **merges** the guest into the signed-in account — reassigns the guest's orders, adds its loyalty,
  and deletes the guest row. (App: the **Account** tab shows the linked phone or a "Link phone"
  control.)
- Either way: a phone already on a **full** account → `409`; phones are digits-only, ≤10.

> **Security caveat — no phone-ownership verification.** Claiming protects *registered accounts*
> (a phone on a full account can't be taken over — `409`), but a **guest** is just unclaimed points
> tied to whatever number a cashier typed; there's **no SMS/OTP check**, so whoever claims a number
> first gets its guest points. To make this airtight (prove the claimant controls the number), add
> an OTP step to register/link-phone before merging. Not implemented yet.

---

## 6. Refunds / cancellation

There's **no automatic money refund** (no eSewa refund integration). An in-store sale is born paid +
`completed`; to reverse it a **manager** cancels the order (`POST /api/orders/manage/{id}/cancel`),
which **restocks** the items and emits a cancel event. In analytics the cancelled-but-paid order is a
**refund**: kept for analysis, excluded from collected revenue. Returning cash to the customer is a
manual counter action.

---

## 7. Analytics

Every order carries `channel`, surfaced as a **`channel` dimension** in the `sales_fact` BigQuery view
(`COALESCE(channel,'online')`). Shareholders can group revenue/units by **online vs in_store** in the
**Explore** tab. Anonymous walk-ins have a `null` `customer_id`, so they don't inflate
`COUNT(DISTINCT customer_id)`. Revenue rule is unchanged: **collected = completed payment AND
`order_status != cancelled`**.

---

## 8. Key endpoints

| Method & path | Purpose |
|---|---|
| `POST /api/orders/manage/in-store-sale` | Ring up a sale → paid, `completed`, `channel=in_store`; deducts stock. Retail only (bulk → 400). Optional `customerId`. |
| `GET /api/orders/manage/customers?q=` | Look up shoppers by **email or phone** to attach (≤10 results). |
| `POST /api/orders/manage/customers` | Quick-create / get a **guest** by `{ phone }` (digits-only, ≤10; idempotent). |
| `POST /api/auth/register` (optional `phone`) | Sign up; **claims** a matching guest (inherits loyalty/orders). |
| `GET /api/system/status` | Public; the till reads the live `memberDiscountRate` from here. |

## 9. Data model touched

- **`Order`** — `Channel` (`online`/`in_store`); `UserId` **nullable** (null = anonymous walk-in).
- **`User`** — `Email`/`PasswordHash` **nullable**, `Phone` (**unique**) → guest accounts.
- **`Payment`** — `ProviderRef = instore:<method>`, `Status = completed`.

## 10. Rules at a glance

- In-store = **retail only**; bulk is rejected.
- Born **paid + completed**; never enters the fulfilment chain.
- Loyalty only for an **attached** customer (walk-in earns nothing).
- Phone: **digits only, ≤10**, unique per customer.
- Guest create + claim are **idempotent** by phone (no duplicates). Claiming can't take over a
  **registered** account (→ `409`), but guest points have **no OTP/ownership check** — see the caveat in §5.
- No payment-gateway call and no auto-refund — money is handled at the counter.
