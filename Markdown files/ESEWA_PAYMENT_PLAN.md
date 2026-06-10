# eSewa Payments — Status & Overview

**Status: built, tested, and deployed to production on eSewa _sandbox_ (`EPAYTEST`).**
No real money moves yet — going live needs a real merchant account (see
[Production checklist](#production-checklist)).

Verified end-to-end against the eSewa sandbox: a real test payment took order #1018
from `pending` → `confirmed`, credited loyalty points, and stored eSewa's reference
(`000FN42`). 39 backend tests pass.

---

## How it works

eSewa **ePay-v2** flow, with the app driving an in-WebView redirect:

1. Customer places an order → it's created as `pending` (no payment yet).
2. App calls `POST /api/payments/esewa/initiate` → backend creates/refreshes a
   `Payment` row and returns a **signed** eSewa form (HMAC-SHA256 over
   `total_amount,transaction_uuid,product_code`).
3. App opens a **WebView** that auto-submits that form to eSewa; customer logs in
   and pays.
4. eSewa redirects back to `…/api/payments/esewa/success` (or `/failure`). The
   backend **verifies the signature** and **double-checks eSewa's status API**
   before confirming, then redirects to a static result page
   (`/payment-success.html` / `/payment-failure.html`).
5. The WebView detects that result page (path-based match, ignoring query strings —
   important so eSewa's own pages don't trip it), closes, and the app shows the
   outcome. As a safety net the app also re-checks `GET /api/orders/{id}` so a paid
   order can never be reported as unpaid.

On success: payment → `completed`, order → `confirmed`, loyalty points credited
(points are credited at **payment**, not at order placement, so unpaid orders earn
nothing). An abandoned/cancelled payment correctly leaves the order `pending`.

---

## What was built

### Backend (`BlueberryMart.Api`)
- `Models/Entities/Payment.cs` + `payment_status` enum + `AddPayments` migration
  (one payment per order; stores amount, status, eSewa `transaction_uuid` and
  `provider_ref`).
- `Configuration/EsewaOptions.cs` — sandbox defaults; prod overrides via `ESEWA__*`
  env vars.
- `Services/EsewaPaymentService.cs` — builds the signed form, verifies the callback
  signature, confirms via eSewa's status API.
- `Controllers/PaymentsController.cs` — `initiate` (auth) + `success` / `failure`
  (anonymous) endpoints.
- `GET /api/orders/{id}` — owner-scoped read of order + payment status (used by the
  app to confirm state after the redirect).
- `wwwroot/payment-success.html` / `payment-failure.html` — result pages.

### Frontend (`BlueberryMartApp`)
- `src/components/EsewaCheckout.tsx` — `react-native-webview` modal that runs the
  eSewa flow and reports the outcome.
- `src/components/ShoppingView.tsx` — opens checkout after an order is placed and
  shows the confirmed/pending result.

### Deploy
- `.github/workflows/deploy.yml` sets the eSewa env vars on Cloud Run
  (`ESEWA__APIBASEURL` + the two result-page deep links).
- Production URL: `https://blueberrymart-api-278293545480.us-central1.run.app`

---

## Sandbox testing

Test login at the eSewa screen (verify current values at
<https://developer.esewa.com.np/pages/Test-credentials> — these **change**; the old
`98068000xx` IDs were retired):

| Field | Value (as of 2026-06) |
|---|---|
| eSewa ID | `9711111111` (or …112/113/114) |
| Password | `Nepal@123` |
| MPIN | `1122` |
| OTP / token | `123456` |

Must complete the **full** flow (login → OTP → final Confirm) or eSewa leaves the
transaction `PENDING` and nothing redirects back.

---

## Production checklist

To take **real** payments, in rough order:

1. **Get a real eSewa merchant account** — apply at
   <https://blog.esewa.com.np/become-a-merchant-v2>. eSewa issues a real
   **merchant/product code** and **secret key** (the live equivalents of `EPAYTEST`
   and `8gBm/:&EnhH.1/q`).
2. **Store the live secrets in GitHub** — add `ESEWA_SECRET_KEY` and
   `ESEWA_MERCHANT_CODE` as repo secrets (never commit the real secret key).
3. **Point config at eSewa's live endpoints** — in the deploy step set:
   - `ESEWA__SECRETKEY=${{ secrets.ESEWA_SECRET_KEY }}`
   - `ESEWA__MERCHANTCODE=${{ secrets.ESEWA_MERCHANT_CODE }}`
   - `ESEWA__FORMURL=https://epay.esewa.com.np/api/epay/main/v2/form`
   - `ESEWA__STATUSURL=https://epay.esewa.com.np/api/epay/transaction/status/`
   (the production hosts, not the `rc`/`rc-epay` sandbox ones).
4. **Re-test** the full flow with a small real payment before announcing.

### Nice-to-haves / follow-ups (not required to go live)
- **Cancel-unpaid-order job** — orders deduct stock at placement as a reservation;
  a background job could cancel long-unpaid orders and restock.
- **Refunds / reconciliation** — no refund flow yet.
- **Retry UX** — re-initiating payment works (fresh `transaction_uuid`); the app
  could offer a "Pay now" button on a pending order instead of only at checkout.
- **Native deep links** — current design intercepts the result page inside the
  WebView (works in Expo Go). A custom `blueberrymart://` scheme would only be
  needed if moving off the WebView approach (requires a dev build).
