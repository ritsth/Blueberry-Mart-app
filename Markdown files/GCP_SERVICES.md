# GCP services — Blueberry Mart

A practical reference for the Google Cloud setup behind Blueberry Mart: what each
service does, how they wire together, and the commands to inspect/change them.
**No secret values live in this file** — only names and structure (it's in a public repo).
The real project ID/number aren't published here either; ask the maintainer if you're a
contributor who needs them (see `CONTRIBUTING.md`).

## Project facts

| Thing | Value |
|---|---|
| Project ID | `<PROJECT_ID>` — ask the maintainer |
| Project number | `<PROJECT_NUMBER>` — ask the maintainer |
| Default region | `us-central1` |

Set your CLI to this project once: `gcloud config set project <PROJECT_ID>`

## The big picture

```
 GitHub push to main ─▶ GitHub Actions ─(Workload Identity Federation, keyless)─▶ GCP
                              │
        ┌─────────────────────┼───────────────────────────┐
        ▼                     ▼                            ▼
  Artifact Registry     Cloud Run                   Firebase Hosting
  (Docker image)   ───▶ blueberrymart-api  ◀── reads secrets from Secret Manager
                          │     │                 (jwt, db conn, admin pw, groq)
                          │     └── connects to ─▶ Cloud SQL (Postgres) via socket
                          └── reads/writes ──────▶ Cloud Storage (review images)
                                                   BigQuery (analytics)
```

---

## 1. Secret Manager

Stores sensitive strings. Cloud Run reads them at deploy time and injects them as env vars,
so secrets never live in code or the image.

**Secrets that exist:**

| Secret | Injected into the API as | Purpose |
|---|---|---|
| `jwt-secret` | `Jwt__Secret` | signs/validates JWT auth tokens |
| `db-connection-string` | `ConnectionStrings__DefaultConnection` | Cloud SQL Postgres connection |
| `admin-password` | `ADMIN__PASSWORD` | bootstrap admin account password |
| `groq-api-key` | `Chat__ApiKey` | Groq LLM (chatbot) |
| `gemini-api-key` | (not currently wired into the API service) | AI key kept for other components |

**Read a secret's value** (you, locally — needs `roles/secretmanager.secretAccessor`):
```bash
gcloud secrets versions access latest --secret=jwt-secret
```

**List secrets / versions:**
```bash
gcloud secrets list
gcloud secrets versions list jwt-secret
```

**Rotate / update a secret** — add a *new version* (old versions stay until disabled):
```bash
echo -n 'NEW_VALUE' | gcloud secrets versions add admin-password --data-file=-
```
> Tip: `echo -n` avoids a trailing newline being stored. For multi-line/binary, use a file
> and delete it after. After rotating a secret the service reads on deploy, redeploy (or
> update) Cloud Run so it picks up `:latest` — see §3.

**Create a new secret:**
```bash
echo -n 'VALUE' | gcloud secrets create my-new-secret --data-file=-
```

## 2. Cloud SQL (PostgreSQL)

The production database. Migrations apply automatically on app startup
(`context.Database.Migrate()`), so deploying new code updates the schema.

| Thing | Value |
|---|---|
| Instance | `blueberrymart-db` |
| Version / tier | `POSTGRES_16` / `db-g1-small` |
| Region | `us-central1` |
| Connection name | `<PROJECT_ID>:us-central1:blueberrymart-db` |

**How Cloud Run reaches it:** the service has a `cloudsql-instances` annotation pointing at
the connection name; Cloud Run mounts a Unix socket at `/cloudsql/<connection-name>`, and the
`db-connection-string` secret connects through it. No public IP / password-over-network.

**Connect from your laptop** (for psql/inspection) — use the Cloud SQL Auth Proxy, which
tunnels securely using your gcloud identity (no DB password exposed on the wire):
```bash
# one-time: download cloud-sql-proxy, then
cloud-sql-proxy <PROJECT_ID>:us-central1:blueberrymart-db &
psql "host=127.0.0.1 dbname=blueberry_mart user=postgres"   # password from the conn-string secret
```

**Inspect the instance:**
```bash
gcloud sql instances describe blueberrymart-db
gcloud sql databases list --instance=blueberrymart-db
```

> Local dev uses a **separate** local Postgres (`Host=localhost`, in the gitignored
> `appsettings.Development.json`) — not this instance.

## 3. Cloud Run

Runs the .NET API as a container, autoscaling from zero.

| Thing | Value |
|---|---|
| Service | `blueberrymart-api` |
| Region | `us-central1` |
| URL | `https://blueberrymart-api-yoh5t4dkqq-uc.a.run.app` |
| Runtime service account | `<PROJECT_NUMBER>-compute@developer.gserviceaccount.com` (default compute SA) |

**Deploys happen via CI** (`.github/workflows/deploy.yml`) on push to `main` — build image →
push to Artifact Registry → `gcloud run deploy`. You normally never deploy by hand.

**Inspect what's running** (image, env, secrets, Cloud SQL link):
```bash
gcloud run services describe blueberrymart-api --region us-central1
```

**Change an env var or secret binding manually** (rarely needed; CI sets most):
```bash
gcloud run services update blueberrymart-api --region us-central1 \
  --update-env-vars "SOME_KEY=value"
gcloud run services update blueberrymart-api --region us-central1 \
  --update-secrets "ADMIN__PASSWORD=admin-password:latest"
```

**Logs:**
```bash
gcloud run services logs read blueberrymart-api --region us-central1 --limit 50
```

Non-secret env currently set: `ASPNETCORE_ENVIRONMENT=Production`, `BigQuery__ProjectId`,
`Gcs__BucketName`, the `ESEWA__*` URLs, `Chat__BaseUrl`/`Chat__Model`, `Cors__PortalOrigins__0/1`
(the Firebase portal URLs), `ADMIN__EMAIL`.

## 4. Artifact Registry

Stores the Docker images Cloud Run runs.

- Repo: `us-central1-docker.pkg.dev/<PROJECT_ID>/blueberrymart/api`
- Tagged by commit SHA + `latest` by CI.
```bash
gcloud artifacts docker images list us-central1-docker.pkg.dev/<PROJECT_ID>/blueberrymart/api
```

## 5. Workload Identity Federation (keyless CI auth)

Lets GitHub Actions authenticate to GCP **without a downloadable service-account JSON key**.
GitHub presents an OIDC token; GCP trusts it and lets the workflow impersonate a service account.

- Deployer SA: `github-actions-deployer@<PROJECT_ID>.iam.gserviceaccount.com`
- GitHub repo secrets: `WIF_PROVIDER`, `WIF_SERVICE_ACCOUNT` (used by both `deploy.yml` and `portal-ci.yml`)
- Roles on that SA: `run.developer`, `artifactregistry.writer`, `iam.serviceAccountUser`,
  `storage.objectViewer`, `firebasehosting.admin`

```bash
# see what the deployer SA can do
gcloud projects get-iam-policy <PROJECT_ID> \
  --flatten="bindings[].members" \
  --filter="bindings.members:github-actions-deployer@<PROJECT_ID>.iam.gserviceaccount.com" \
  --format="value(bindings.role)"
```

## 6. Cloud Storage (GCS)

- Bucket `blueberrymart-review-images-<PROJECT_NUMBER>` — customer review photos. The API writes
  here and returns absolute `https://storage.googleapis.com/...` URLs.

## 7. Firebase Hosting

- Hosts the back-office portal (`BlueberryMartPortal/`) at **https://blueberrymart-admin.web.app**
  (site id `blueberrymart-admin`). Auto-deployed by `portal-ci.yml` via the same WIF identity.
  Same GCP project as everything else. See [CICD_pipeline.md](CICD_pipeline.md).

## 8. BigQuery

- Analytics warehouse (`sales_fact`) for the shareholder self-service reporting. `BigQuery__ProjectId`
  on the API points at the project. ETL runs under `bbm-analytics-etl@…` SA.

---

## Cheat sheet

```bash
# who am I / which project
gcloud auth list
gcloud config get-value project

# read a secret
gcloud secrets versions access latest --secret=<name>

# what's deployed
gcloud run services describe blueberrymart-api --region us-central1
gcloud run services logs read blueberrymart-api --region us-central1 --limit 50

# DB access from laptop
cloud-sql-proxy <PROJECT_ID>:us-central1:blueberrymart-db

# list service accounts and a SA's roles
gcloud iam service-accounts list
```

## Security model (how this project keeps secrets safe)

- **Secrets never in code or images** — they live in Secret Manager and are injected as env
  vars at deploy time. Code reads `IConfiguration` (e.g. `Jwt:Secret`), unaware of the source.
- **Keyless CI** — GitHub uses Workload Identity Federation, so there's no long-lived JSON key
  to leak. Prefer this over `gcloud iam service-accounts keys create` whenever possible.
- **Least privilege** — the deployer SA has only the roles it needs (deploy + push images +
  host), not Owner.
- **Local vs prod isolation** — local dev uses a separate local Postgres and a gitignored
  `appsettings.Development.json`; prod credentials only exist in Secret Manager.
- If a secret ever leaks, **rotate it** (add a new version, redeploy, disable the old version).

---

## Learning material (concepts worth knowing)

Roughly in the order they'll help you here:

1. **IAM & service accounts** — the foundation. Members (users, service accounts) get *roles*
   (bundles of permissions) on *resources*. A "service account" is a non-human identity that
   apps/CI act as. → search "GCP IAM overview", "service accounts explained".
2. **Application Default Credentials (ADC)** — how Google client libraries find credentials
   automatically (env var, gcloud login, or the attached service account on Cloud Run). Why
   the API can call GCS/BigQuery without any key in code. → "Google ADC".
3. **Secret Manager** — versioned secrets, `:latest`, IAM-gated access. → "Secret Manager best practices".
4. **Cloud Run** — serverless containers, scale-to-zero, the runtime service account, env vars
   vs mounted secrets, revisions/rollbacks. → "Cloud Run concepts".
5. **Cloud SQL connectivity** — public IP vs private IP vs the **Auth Proxy / connectors**, and
   why the proxy (identity-based, encrypted) beats password-over-IP. → "Connecting to Cloud SQL securely".
6. **Workload Identity Federation** — OIDC trust so CI needs no key. The modern replacement for
   downloaded SA keys. → "Workload Identity Federation with GitHub Actions".
7. **Artifact Registry** — private Docker/registry, image tags, who can push/pull.
8. **gcloud basics** — `gcloud config`, `--format`/`--filter` (jq-like output control),
   `describe` vs `list`, `--impersonate-service-account`.

**Mental model to internalize:** *identity → permission → resource*. Almost every "permission
denied" in GCP is "this identity is missing this role on this resource." When something can't
deploy, read, or connect, ask: which identity is it running as, and what role does it need?
