# BlueberryMartPortal — back-office web app

React 18 + TypeScript + Vite SPA for staff/managers/admins. Talks to the same .NET API; **admin
role is the real security boundary** (server-enforced — keeping admin code out of the mobile app
is only defence-in-depth). Auto-deploys to **Firebase Hosting**.

## Commands (run from this folder)
- `npm run dev` — Vite dev server
- `npm run build` — `tsc -b && vite build`
- Deploy: Firebase (see `firebase.json`); config in `.env` / `.env.production` (gitignored where
  sensitive).

## Layout
- `src/api.ts` — fetch wrapper + endpoint calls. `src/auth.ts` — token handling.
- `src/pages/` — one component per back-office screen: `Dashboard`, `ItemsPage` (catalogue +
  photo upload), `OrdersPage` (fulfillment + record-payment/cancel), `ReviewsPage`,
  `UsersPage` (roles/ban), `ReportsPage`, `SettingsPage`, `Login`.
- `src/components/`, `src/styles.css`.

## Notes
- API base URL comes from a Vite env var (`import.meta.env.VITE_*`); see `.env.example`.
- Back-office actions map to `Admin`/`Manager`/`Staff`-gated API endpoints (`AdminController`,
  `ManageOrdersController`, `ManageInventoryController`). Staff/manager are branch-scoped.
- This is separate from the customer Expo app (`BlueberryMartApp/`).
