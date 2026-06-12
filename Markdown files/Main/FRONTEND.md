# Blueberry Mart — Frontend Overview

The frontend is an **Expo / React Native** app (`BlueberryMartApp/`) — the customer
and shareholder mobile client for Blueberry Mart. It talks to the .NET backend over
REST (see `BACKEND.md`).

## Stack
- **Expo SDK ~54**, **React Native 0.81**, TypeScript (strict).
- React Navigation (native stack + bottom tabs).
- `@react-native-async-storage/async-storage` for the JWT.
- `react-native-webview` (eSewa checkout), `expo-image-picker` (review photos),
  `react-native-safe-area-context`.

## Structure (`src/`)
```
screens/
  LoginScreen.tsx        email/password login (+ link to sign up)
  RegisterScreen.tsx     create a customer account
  AddressesScreen.tsx    manage delivery addresses
  ReviewScreen.tsx       write a review (rating, comment, optional photo)
  tabs/
    CustomerShopTab.tsx     regular shopping (wraps ShoppingView)
    BulkTab.tsx             members-only bulk catalog (wraps ShoppingView)
    ActivityTab.tsx         order history + reviews; pay/review actions
    AccountTab.tsx          profile, loyalty, membership
    ShareholderHomeTab.tsx  shareholder analytics (fixed dashboards)
    ExploreTab.tsx          self-service analytics — build/save custom charts
components/
  ShoppingView.tsx       branch list, search, cart, checkout (regular + bulk)
  EsewaCheckout.tsx      eSewa payment WebView modal
services/
  authService.ts         login, JWT storage, role parsing
  analyticsService.ts    Explore catalog / query / saved-report calls
navigation/
  CustomerTabs.tsx       bottom tabs for customers
  ShareholderTabs.tsx    bottom tabs for shareholders
```
`App.tsx` defines the root native-stack: `Login`, `CustomerTabs`,
`ShareholderTabs`, `ReviewScreen`, `AddressesScreen`.

## Navigation & roles
On login the JWT's role claim is parsed (`authService.parseRole`) → routed to
`CustomerTabs` or `ShareholderTabs`. `ReviewScreen` and `AddressesScreen` are pushed
on top of the tabs from within tab screens via `useNavigation`.

## Auth flow
`authService.login()` POSTs to `/api/auth/login`; `authService.register()` POSTs to
`/api/auth/register` (creates a customer and returns a token, just like login). Both
store `jwt_token` + `user_role` in AsyncStorage. The login screen links to the
register screen ("Don't have an account? Sign up") and vice versa. Every API call
attaches `Authorization: Bearer <token>` and reads the token with `getStoredToken()`.

## Key flows
- **Shopping** (`ShoppingView`): pick a branch → browse/search inventory → add to a
  per-branch cart → choose pickup or delivery (address required for delivery) →
  place order. The same component powers the regular and bulk (members-only) tabs.
- **Checkout + payment**: placing an order creates it as `pending`; the app then
  opens `EsewaCheckout` — a WebView that auto-submits the signed eSewa form, runs
  the payment, and closes when it lands on the backend's result page
  (`/payment-success.html` / `/payment-failure.html`). It then double-checks
  `GET /api/orders/{id}` before reporting success/failure.
- **Activity** (`ActivityTab`): lists past orders (from `/api/profile`) and reviews.
  - Pending (unpaid) orders are tagged **"Not paid"** with a **Pay now with eSewa**
    button (re-opens `EsewaCheckout`).
  - Paid orders show a **Write a review** button → `ReviewScreen` for that order.
- **Reviews** (`ReviewScreen`): pick an item from the order, rate 1–5, comment,
  optional photo (camera or library); submits multipart to `/api/reviews` and earns
  loyalty points.
- **Membership / Account**: activate/cancel Blueberry Plus, view loyalty points and
  membership status.
- **Shareholder**: fixed analytics dashboard with charts (`ShareholderHomeTab`), plus
  a self-service **Explore** tab.
- **Explore** (`ExploreTab` + `analyticsService`): a self-service report builder for
  shareholders over the BigQuery warehouse. Fetches `/api/analytics/catalog` and renders
  catalog-driven pickers (measures + aggregation, group-by dimensions incl. `order_status`,
  time range, chart type, and a "Collected revenue only" toggle = payment `completed` AND
  `order_status != cancelled`), POSTs the spec to `/api/analytics/query`, and renders a Bar/Line/Pie
  chart (`react-native-chart-kit`) with a scrollable data-table fallback. Charts can be
  **saved** (config only) to `/api/analytics/reports` and re-loaded/re-run against fresh
  data. When BigQuery isn't configured (e.g. production), the catalog reports
  `enabled:false` and the tab shows a "warehouse not configured" state.

## API configuration
The base URL comes from `EXPO_PUBLIC_API_URL`:
- `.env.local` (gitignored) sets it per machine. Production value is the Cloud Run
  URL `https://blueberrymart-api-278293545480.us-central1.run.app`.
- For local backend testing, point it at a local API exposed via a public tunnel
  (e.g. cloudflared), since eSewa must be able to reach the success/failure URLs.

## Run
```bash
cd BlueberryMartApp
npm install
npx expo start        # scan the QR with Expo Go
npx tsc --noEmit      # type check (also enforced in CI)
```

## CI
A **Frontend Type Check** GitHub Actions workflow runs `tsc` on pushes that touch
the app. The app is delivered via Expo Go / a dev build (not part of the backend
Cloud Run deploy).
