# Security & System-Design Posture

A pre-ship checklist for the Blueberry Mart app, with **where we stand on each point**.
Inspired by the "6 questions before you ship" checklist, expanded with the broader
production-readiness items.

**Status legend:** ✅ Done · ⚠️ Partial / acceptable for now · 🔜 Planned (deferred) · ❌ Missing

_Last reviewed: 2026-06-25 (P2 batch 2 — CSP shipped report-only on the hosted pages; CI-secret
cleanup reviewed and deliberately skipped as non-prod test values. Batch 1 = `df6b2f2`:
Dependabot, request-body cap, CORS; on top of the P0+P1 pass in `eb2e30d`)._

---

## The 6 core questions

### 1. Authorization — *Can users access data that isn't theirs?* ✅
- Every user-owned resource (orders, addresses, reviews, notifications, profile) is filtered by the
  user id from the JWT, never an id taken from the URL or body. No IDOR found in audit.
- eSewa payment callbacks are verified by signature **and** the amount is re-confirmed with eSewa
  before crediting an order.
- **Where we are:** Solid. This is the strongest area.

### 2. Rate limiting — *Can an attacker spam or abuse the APIs?* ✅ (was ⚠️)
- Auth endpoints (login/register/forgot-password/resend-verification/google): **5 req/60s per IP**.
- `/api/chat` (LLM, costs money): **per-user** throttle added.
- **Global per-IP backstop** added so every endpoint has a limit (config-driven).
- **Known limit:** the per-IP key uses `X-Forwarded-For`, which is spoofable behind Cloud Run — a
  speed bump, not a hard identity boundary. Acceptable for our threat level.
- **Where we are:** Good after this pass; previously only auth was covered.

### 3. Secrets management — *Are keys/credentials exposed anywhere?* ✅
- Prod secrets (JWT, Resend, eSewa, Kafka, BigQuery) live in **Google Secret Manager**, injected via
  **Workload Identity Federation** — no long-lived keys in the repo or git history.
- `appsettings.Development.json` and `.env.local` are gitignored.
- **Reviewed, no action (2026-06-25):** the committed CI/test Postgres password and eSewa
  **sandbox** key are both non-production test values (the eSewa one is eSewa's publicly
  documented sandbox HMAC key, used as a test vector). Not real secrets; left as-is. Just don't
  reuse that DB password anywhere real.
- **Where we are:** Good.

### 4. Access control — *Can a user modify requests to gain access?* ✅
- Role guards (`[Authorize(Roles=…)]`) on every privileged endpoint; staff/manager are branch-scoped
  via a `GuardBranch()` helper.
- **No mass assignment:** `Role`, `LoyaltyPoints`, `EmailVerified`, `BranchId`, ban/delete flags are
  not user-settable. Role changes are admin-only and whitelisted.
- **Where we are:** Solid.

### 5. Token security — *If a JWT is stolen, can it be revoked?* ✅ (was ⚠️)
- 8-hour HS256 token; all validation flags on (issuer/audience/lifetime/signature); HTTPS enforced.
- Strong password hashing: **PBKDF2-SHA256, 120k iterations**, per-password salt, constant-time
  compare, transparent upgrade of legacy hashes.
- **Per-request revocation:** ban or account-deletion kills the token on the *next request* (DB
  re-check in the auth pipeline).
- **New:** resetting the password now invalidates all older tokens (`PasswordChangedAt` + `iat`).
- **New:** mobile JWT moved to encrypted **SecureStore** (Keychain/Keystore) — *ships in app v8*.
- **Deferred (🔜):** no refresh-token rotation (single 8h token). Fine at current scale.
- **Where we are:** Good baseline, now hardened.

### 6. Resilience — *Can one request bring down the system?* ✅ (was ⚠️)
- **New:** timeouts on every outbound call (eSewa/Resend/LLM/Google) so a hung upstream can't pin
  threads.
- **New:** image uploads capped at 5 MB with magic-byte validation.
- Bounded by design: BigQuery query builder (max measures/dimensions, required date filter,
  parameterized), chat history capped (20 msgs / 2000 chars), API/worker split isolates background
  load.
- **New:** explicit global request-body-size cap (10 MB `MultipartBodyLengthLimit`) so an oversized
  multipart request is rejected at the Kestrel level, before reaching any controller.
- **Deferred (🔜):** pagination on a couple of list endpoints (notifications, shareholder
  inventory). Low risk at tester volume.
- **Where we are:** The previously weakest area is now in good shape.

---

## Broader production-readiness

### Data & database
- **Migrations** ✅ — EF Core, version-controlled, auto-applied on deploy.
- **PK/UUIDs, UTC timestamps** ✅ — consistent conventions.
- **Backups / PITR** ⚠️ — confirm Cloud SQL automated backups + point-in-time recovery are enabled.
- **Destructive-migration safety** 🔜 — auto-migrate-on-startup is convenient; gate destructive
  migrations so a bad one can't run unattended.

### Transport & web
- **HTTPS everywhere** ✅ — enforced on client and server; no HTTP fallback.
- **CORS** ✅ — confirmed scoped via `WithOrigins(portalOrigins)` (config-driven admin-portal
  origin); no `AllowAnyOrigin()` anywhere.
- **Security headers** ⚠️ — CSP added to the hosted pages in **report-only mode**
  (`Content-Security-Policy-Report-Only`, set via `UseStaticFiles` `OnPrepareResponse` so API JSON
  is untouched). Logs violations without blocking; flip to enforcing (`Content-Security-Policy`)
  once the live reset/payment pages are confirmed clean in the browser console. HSTS already via
  HTTPS redirect.

### Reliability & correctness
- **Idempotency** 🔜 — add idempotency keys on order/payment submit to survive double-taps and
  callback retries.
- **Transactions** ✅ — order placement, payments, stock adjustments use DB transactions with
  rollback.
- **Event pipeline** ✅ — sales/stock events via a transactional outbox → Kafka → BigQuery.

### Observability & ops
- **Health endpoint** ✅ — `/health`.
- **Error tracking** 🔜 — wire up Sentry / Cloud Error Reporting for stack traces + alerting.
- **Cost alerts** 🔜 — LLM, email, and BigQuery are pay-per-use; set a billing budget + alert.
- **Cloud Run guardrails** 🔜 — set max-instances / concurrency and a cost ceiling.

### Supply chain & code
- **CodeQL** ✅ — runs on every push.
- **Dependency scanning** ✅ — Dependabot enabled for npm + NuGet (weekly, `.github/dependabot.yml`).
- **CI gates** ✅ — `dotnet format` + full test suite must pass.

### Privacy & compliance
- **Account deletion** ✅ — soft-delete + PII scrubbing (Google Play requirement).
- **Email verification** ✅ — verify-before-login, link-based.
- **Email deliverability** ✅ — SPF/DKIM/DMARC on `blueberrymart.shop` via Resend.
- **Data export** 🔜 — optional "download my data" for fuller GDPR coverage.

---

## Quick "what to do next" (deferred backlog, roughly prioritized)
_Done in batch 1 (`df6b2f2`): Dependabot ✅ · request-body-size cap ✅ · CORS re-verify ✅._

1. **(GCP console)** Confirm Cloud SQL backups/PITR are on; gate destructive migrations.
2. **(GCP console)** Add error tracking + a billing budget alert.
3. **(GCP console)** Set Cloud Run max-instances / concurrency / cost ceiling.
4. Pagination on the unbounded list endpoints.
5. Idempotency keys on order/payment. _(CSP shipped report-only — flip to enforcing after a live
   browser-console check of the reset/payment pages.)_
6. ~~Stop committing the CI Postgres / eSewa-sandbox secrets.~~ **Decided not worth it
   (2026-06-25):** both are non-production test values — the Postgres password is a throwaway
   CI-container password, and the eSewa "secret" is eSewa's *publicly documented* sandbox HMAC key
   used as a test vector. No real secret is exposed; moving them to GitHub secrets adds test
   friction for ~zero gain.
7. (Later, if scale demands) refresh-token rotation; cache the per-request user lookup.
