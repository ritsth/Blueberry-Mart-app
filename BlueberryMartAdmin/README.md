# Blueberry Mart — Admin Portal

A standalone **React + Vite + TypeScript** web app for administrators. It is
deliberately **separate from the mobile app** so admin code never ships in the public
mobile binary. It talks to the **same .NET API** as the mobile app; the real security
boundary is the API's `[Authorize(Roles = "Admin")]` on `/api/admin/*`, plus
per-request ban enforcement — this portal is just a convenient UI.

## What it does (v1)

- **Login** — uses the shared `/api/auth/login`; only accounts with the `admin` role
  are allowed in.
- **Users** — search/filter, **ban / unban** (takes effect immediately — the API
  rejects the banned user's existing token on the next request), and **change a user's
  role** (customer/shareholder/admin; staff/manager are accepted but inert until the
  Wave 2 roles ship). The last remaining admin can't be demoted.
- **Reviews** — list recent reviews and **delete** inappropriate ones.
- **Settings** — edit formerly-hardcoded values (delivery fee, membership fee, member
  discount) and toggle **maintenance mode**, which pauses customer ordering (the API
  returns 503; the app can read `/api/system/status` for a banner).

## Run locally

```bash
cp .env.example .env      # point VITE_API_URL at your API (default http://localhost:5027)
npm install
npm run dev               # http://localhost:5173
```

The API allows `http://localhost:5173` via CORS (`Cors:PortalOrigins` in
`appsettings.json`). You need an admin account — the API auto-creates one on startup
from `Admin:Email` / `Admin:Password` (set those in the API's
`appsettings.Development.json` locally, or Secret Manager in prod).

## Build & deploy (Firebase Hosting)

The portal deploys as a static site to **Firebase Hosting** (same GCP project as the
API: `project-76ca6efe-7878-4dc8-bff`). Config is in `firebase.json` (SPA rewrite +
asset caching) and `.firebaserc` (default project). The prod API URL is baked in from
`.env.production` at build time.

**One-time setup** (per machine):
```bash
npm i -g firebase-tools      # or rely on npx (the deploy script uses npx)
firebase login               # run yourself: ! firebase login
# Ensure Firebase is enabled on the project (once):
#   https://console.firebase.google.com  -> Add project -> pick the existing GCP project
```

**Deploy:**
```bash
npm run deploy               # = npm run build && firebase deploy --only hosting
```
This publishes to `https://blueberrymart-admin.web.app`
(and `…firebaseapp.com`).

### Wire the prod API to the portal (run once, after first deploy)

The API must (a) allow the portal's origin via CORS and (b) have a bootstrap admin.
These set secrets/config on Cloud Run — **run them yourself** (the password lives only
in Secret Manager, never in git):

```bash
# 1) Allow the portal origins (CORS)
gcloud run services update blueberrymart-api --region us-central1 \
  --update-env-vars '^@^Cors__PortalOrigins__0=https://blueberrymart-admin.web.app@Cors__PortalOrigins__1=https://blueberrymart-admin.firebaseapp.com'

# 2) Bootstrap admin — email as env, password as a Secret Manager secret
echo -n 'A_STRONG_PASSWORD' | gcloud secrets create admin-password --data-file=-   # first time
# (later rotations: gcloud secrets versions add admin-password --data-file=-)
gcloud run services update blueberrymart-api --region us-central1 \
  --update-env-vars ADMIN__EMAIL=you@yourdomain.com \
  --update-secrets ADMIN__PASSWORD=admin-password:latest
```

On the next request the API creates the admin (or promotes the existing email). Log
into the portal with that email + the secret password.

## Roles roadmap

v1 ships the global **admin** role only. Branch-scoped **staff** / **manager** roles
(and the operations screens they need) are a deliberate later wave — they require a
branch association on users and scope-enforcing authorization, built when there are
actually store staff to onboard.
