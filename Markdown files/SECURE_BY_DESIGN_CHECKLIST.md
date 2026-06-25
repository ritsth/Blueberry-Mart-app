# Secure-by-Design & Good-Architecture Checklist

A **general, reusable** checklist for any web/mobile/API project — the questions to ask *before*
shipping, and the architecture habits that keep a system maintainable as it grows. Not tied to any
one app. Use it as a pre-launch review or a periodic health check.

> How to use: walk each section, mark every line ✅ done / ⚠️ partial / 🔜 planned / ❌ missing / —
> not applicable. Anything ⚠️/❌ on a P0/P1 item should block a public launch.

---

## Part 1 — The 6 core security questions (ask these first)

These catch the highest-impact, most common real-world breaches.

1. **Authorization — can a user access data that isn't theirs?**
   - Every owned resource is filtered by the identity in the auth token, **never** by an id taken
     from the URL/body. (Prevents IDOR — the #1 real-world API bug.)
   - Test it: log in as user A, try to fetch/modify user B's resource by id → must 403/404.

2. **Rate limiting — can someone spam or abuse the APIs?**
   - Auth endpoints (login/register/reset/verify) throttled per IP.
   - Anything that **costs money** (LLM, email, SMS, third-party APIs) throttled per user.
   - A global backstop limit so every endpoint has *some* ceiling.

3. **Secrets management — are keys/credentials exposed anywhere?**
   - No secrets in the repo or git history (scan history, not just current files).
   - Prod secrets in a vault / secret manager, injected at runtime — not in code or images.
   - Local/dev secret files are gitignored. Rotate anything ever committed.

4. **Access control — can a user escalate privileges?**
   - Role/permission checks on every privileged endpoint (default-deny, not default-allow).
   - **No mass assignment:** fields like `role`, `isAdmin`, `balance`, `verified`, `ownerId` are
     server-controlled and *not* settable from the request body.

5. **Token / session security — if a token is stolen, can it be revoked?**
   - Short-lived tokens; all validation on (issuer/audience/expiry/signature); HTTPS only.
   - Strong password hashing (bcrypt/scrypt/argon2/PBKDF2 with high iterations + per-user salt,
     constant-time compare). Never plain/SHA-only.
   - A revocation path: ban/delete/password-change invalidates existing tokens.
   - On mobile, tokens live in the OS keystore/keychain (encrypted), not plain local storage.

6. **Resilience — can one request bring down the system?**
   - Timeouts on **every** outbound call (DB, HTTP, queue) — a hung upstream must not pin threads.
   - Input size caps (uploads, request bodies, batch sizes) and content validation
     (validate by magic bytes / actual type, not just the declared Content-Type).
   - Bounded everything: pagination on list endpoints, max page size, query/result limits.

---

## Part 2 — Input handling & common web vulns

- **Validate all input** server-side (length, type, range, format) — never trust the client.
- **Injection:** parameterized queries / ORMs only; never string-concatenate SQL or shell commands.
- **XSS:** escape/encode output; use a templating layer that auto-escapes; set a Content-Security-Policy.
- **CSRF:** for cookie-based auth, use anti-CSRF tokens or `SameSite` cookies. (Token-in-header auth
  is largely immune.)
- **File uploads:** validate type by content, cap size, store outside the web root, randomize names,
  never execute uploaded files.
- **Open redirects / SSRF:** validate and allowlist any user-supplied URL before the server fetches it.
- **Mass assignment:** bind requests to explicit DTOs, not directly to DB entities.

---

## Part 3 — Transport, headers & network

- **HTTPS everywhere**, no HTTP fallback; HSTS enabled.
- **CORS** scoped to known origins — never `*` with credentials.
- **Security headers:** CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options`/frame-ancestors,
  `Referrer-Policy`.
- **No sensitive data in URLs** (they land in logs, history, referrers) — use headers/body.
- Internal services not exposed to the public internet; least-privilege network rules / firewall.

---

## Part 4 — Data & database

- **Migrations** version-controlled and reviewed; **destructive migrations gated** so a bad one
  can't run unattended on deploy.
- **Backups + point-in-time recovery** enabled *and tested* (an untested backup is a guess).
- **Encryption** at rest (managed by the DB/provider) and in transit.
- **PII minimization:** collect only what you need; know where every piece of PII lives.
- **Soft-delete vs hard-delete** policy is deliberate; retention windows defined.
- Consistent conventions: stable primary keys (UUIDs), UTC timestamps, explicit nullability.

---

## Part 5 — Reliability & correctness

- **Idempotency keys** on create/payment endpoints so double-taps and retries don't double-charge.
- **Transactions** around multi-step writes, with rollback on failure.
- **Concurrency:** optimistic locking / version columns where two writers can collide.
- **Graceful degradation:** if a non-critical dependency is down, the core path still works.
- **Retries with backoff + jitter** on transient failures — but only on idempotent operations.
- **Dead-letter / outbox** patterns for events so nothing is silently lost.

---

## Part 6 — Observability & operations

- **Structured logging** with correlation/request ids; **never log secrets, tokens, or full PII.**
- **Error tracking** (Sentry / Cloud Error Reporting) with alerting on spikes.
- **Metrics + health checks** (`/health`, readiness vs liveness) wired to your platform.
- **Cost alerts / budgets** on any pay-per-use service (LLM, email, data warehouse, egress).
- **Resource guardrails:** max instances / concurrency / autoscaling ceilings to cap blast radius.
- **Runbooks** for the obvious incidents (DB down, secret rotation, rollback a bad deploy).

---

## Part 7 — Supply chain & code quality

- **Dependency scanning** (Dependabot / Renovate) for known CVEs; patch on a cadence.
- **SAST** (CodeQL or similar) on every push.
- **CI gates:** formatting/lint + full test suite must pass before merge/deploy.
- **Pin dependencies** (lockfiles committed); review what you pull in (typosquatting, abandonware).
- **Least-privilege CI:** scoped deploy credentials, ideally workload identity over long-lived keys.

---

## Part 8 — Privacy & compliance

- **Account deletion** path (soft-delete + PII scrubbing) — often an app-store/GDPR requirement.
- **Data export** ("download my data") for fuller GDPR/CCPA coverage.
- **Consent** for tracking/analytics; default to the privacy-preserving option.
- **Email deliverability + anti-spoofing:** SPF, DKIM, DMARC on any sending domain.
- **Privacy policy + terms** published and linked where required.
- **Age / content ratings** correct for the platforms you ship to.

---

## Part 9 — Architecture & design habits (keeps it maintainable as it grows)

- **Layered separation:** controllers/handlers stay thin → delegate to services → data access in
  repositories. Business logic doesn't live in the HTTP layer.
- **Single source of truth** for each piece of data; avoid duplicated/derived state that can drift.
- **Stateless services** where possible (state in the DB/cache) so you can scale horizontally.
- **Config over hardcoding:** environment-specific values come from config/secrets, not literals.
- **Explicit contracts:** DTOs for requests/responses; don't leak DB entities to the wire.
- **Async/event-driven** for slow or cross-cutting work (queues, outbox, background workers) so the
  request path stays fast.
- **Fail loud in dev, degrade gracefully in prod.** Validate config at startup and refuse to boot
  if a required secret is missing.
- **Document the non-obvious:** a short architecture doc + per-module notes (the "why", not the
  "what") saves the next person — including future you.
- **Design for testability:** dependency injection, interfaces at the seams, integration tests that
  exercise real flows end-to-end.

---

## How to prioritize (if you can't do it all before launch)

- **P0 (block launch):** authorization/IDOR, secrets exposure, password hashing, HTTPS, input
  validation/injection, outbound timeouts.
- **P1 (block public/scale):** rate limiting, token revocation, backups+PITR, cost alerts, access
  control / mass-assignment, error tracking.
- **P2 (fast follow):** idempotency, pagination, CSP/security headers, dependency scanning,
  data export, refresh-token rotation, runbooks.

> Security is never "done" — it's a posture you re-check each release. Re-run this list whenever you
> add a new endpoint, a new third-party dependency, or a new data type.
