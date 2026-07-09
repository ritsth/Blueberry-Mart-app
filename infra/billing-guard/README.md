# Billing guard — budget-triggered auto-stop

Stops billable resources automatically if spend reaches 100% of the monthly budget, so a
runaway loop (chat spam, a bug hammering BigQuery, etc.) can't rack up cost unattended while
you're asleep — you don't have to rely on the budget email alone.

**What it does at 100% of budget:**
1. Scales `blueberrymart-api` and `blueberrymart-worker` (Cloud Run) to **0/0** instances —
   stops new requests (chat/email/BigQuery-triggered work) within about a minute.
2. Stops the `blueberrymart-db` Cloud SQL instance (`activationPolicy=NEVER`) — this is the
   biggest fixed cost since it bills 24/7 regardless of traffic, unlike Cloud Run.

**What it does NOT do:** delete data, disable billing, or touch anything else in the project.
Both actions are reversible in seconds — see "Resume service" below.

You get the existing email alerts at 50/90/100/120% regardless of this function; this only adds
an automatic action at 100%.

All values below are specific to this project (confirmed via `gcloud` on 2026-07-08):

| Value | Setting |
|---|---|
| Project ID | `project-76ca6efe-7878-4dc8-bff` |
| Project number | `278293545480` |
| Billing account | `01F9E2-8429F1-9A7221` |
| Region | `us-central1` |
| Cloud Run services | `blueberrymart-api`, `blueberrymart-worker` |
| Cloud SQL instance | `blueberrymart-db` |

---

## 1. Enable the APIs you'll need

```bash
gcloud config set project project-76ca6efe-7878-4dc8-bff

gcloud services enable \
  cloudfunctions.googleapis.com \
  eventarc.googleapis.com \
  pubsub.googleapis.com \
  run.googleapis.com \
  sqladmin.googleapis.com \
  cloudbilling.googleapis.com \
  billingbudgets.googleapis.com
```

## 2. Create the Pub/Sub topic

```bash
gcloud pubsub topics create billing-budget-alerts
```

## 3. Create a dedicated, narrowly-scoped service account for the function

Deliberately not reusing the default compute service account — this one can only touch the two
Cloud Run services and Cloud SQL, nothing else.

```bash
gcloud iam service-accounts create billing-guard-sa \
  --display-name="Billing budget auto-stop"

# Cloud Run: grant per-service, not project-wide
gcloud run services add-iam-policy-binding blueberrymart-api \
  --region=us-central1 \
  --member="serviceAccount:billing-guard-sa@project-76ca6efe-7878-4dc8-bff.iam.gserviceaccount.com" \
  --role="roles/run.developer"

gcloud run services add-iam-policy-binding blueberrymart-worker \
  --region=us-central1 \
  --member="serviceAccount:billing-guard-sa@project-76ca6efe-7878-4dc8-bff.iam.gserviceaccount.com" \
  --role="roles/run.developer"

# Cloud SQL has no per-instance IAM binding, only project-level — this is the one broader grant
# (roles/cloudsql.editor can resize/restart instances project-wide; there's no narrower
# predefined role for just "patch activation policy").
gcloud projects add-iam-policy-binding project-76ca6efe-7878-4dc8-bff \
  --member="serviceAccount:billing-guard-sa@project-76ca6efe-7878-4dc8-bff.iam.gserviceaccount.com" \
  --role="roles/cloudsql.editor"
```

## 4. Deploy the Cloud Function

From the repo root:

```bash
gcloud functions deploy billing-guard \
  --gen2 \
  --runtime=python312 \
  --region=us-central1 \
  --source=infra/billing-guard \
  --entry-point=budget_alert \
  --trigger-topic=billing-budget-alerts \
  --service-account=billing-guard-sa@project-76ca6efe-7878-4dc8-bff.iam.gserviceaccount.com \
  --set-env-vars=PROJECT_ID=project-76ca6efe-7878-4dc8-bff,REGION=us-central1,CLOUD_RUN_SERVICES=blueberrymart-api,SQL_INSTANCE=blueberrymart-db,TRIGGER_RATIO=1.0,BUDGET_DISPLAY_NAME="Blueberry Mart monthly"
```

> Note: `CLOUD_RUN_SERVICES` is comma-separated if you list more than one — `gcloud`'s
> `--set-env-vars` splits on commas, so if you need both services in that var, use
> `--set-env-vars=^:^PROJECT_ID=...:CLOUD_RUN_SERVICES=blueberrymart-api,blueberrymart-worker:...`
> (custom delimiter `:`) or pass a `--env-vars-file=env.yaml` instead — simplest is a YAML file:
> ```yaml
> PROJECT_ID: project-76ca6efe-7878-4dc8-bff
> REGION: us-central1
> CLOUD_RUN_SERVICES: blueberrymart-api,blueberrymart-worker
> SQL_INSTANCE: blueberrymart-db
> TRIGGER_RATIO: "1.0"
> BUDGET_DISPLAY_NAME: "Blueberry Mart monthly"
> ```
> then `--env-vars-file=infra/billing-guard/env.yaml` instead of `--set-env-vars`. Use whichever
> `gcloud` accepts cleanly — the exact env-var flag syntax varies slightly by `gcloud` version;
> if it errors, run `gcloud functions deploy --help` and adjust.

If the deploy warns about a missing Eventarc/Pub/Sub invoker binding, it will print the exact
`gcloud` command to fix it — run that command as shown.

## 5. Create the budget, linked to the topic

```bash
gcloud billing budgets create \
  --billing-account=01F9E2-8429F1-9A7221 \
  --display-name="Blueberry Mart monthly" \
  --budget-amount=30USD \
  --filter-projects=projects/278293545480 \
  --threshold-rule=percent=0.5 \
  --threshold-rule=percent=0.9 \
  --threshold-rule=percent=1.0 \
  --threshold-rule=percent=1.2 \
  --notifications-rule-pubsub-topic=projects/project-76ca6efe-7878-4dc8-bff/topics/billing-budget-alerts
```

This keeps the email alerts at 50/90/100/120% (default recipients = billing account admins,
i.e. you) *and* delivers every notification to the Pub/Sub topic. The function itself decides
to act only once actual spend reaches 100% (`TRIGGER_RATIO=1.0`) — the 50%/90% notifications
still just email you, no action taken.

---

## Verify it works — WITHOUT waiting for real spend

This publishes a fake "over budget" message straight to the topic. **It will actually scale
your services down and stop Cloud SQL for real** — do this when you're ready to also practice
the resume steps below, not by accident.

```bash
gcloud pubsub topics publish billing-budget-alerts \
  --message='{"budgetDisplayName":"Blueberry Mart monthly","costAmount":31,"budgetAmount":30,"currencyCode":"USD"}'
```

Then check:
```bash
# Cloud Function logs — should show "Stopping billable resources", then the two actions
gcloud functions logs read billing-guard --region=us-central1 --gen2 --limit=20

# Confirm the services actually scaled down
gcloud run services describe blueberrymart-api --region=us-central1 --format="value(spec.template.metadata.annotations['autoscaling.knative.dev/minScale'],spec.template.metadata.annotations['autoscaling.knative.dev/maxScale'])"
gcloud sql instances describe blueberrymart-db --format="value(settings.activationPolicy)"
```

Publish it again — the second run should log "already scaled to 0/0" / "already stopped" and
make no further changes (idempotency check).

---

## Resume service (after you've dealt with whatever caused the spend)

Restores the exact configuration confirmed before this was set up (2026-07-08):

```bash
# Cloud SQL back on
gcloud sql instances patch blueberrymart-db --activation-policy=ALWAYS

# blueberrymart-api: min 0 / max 3 (original)
gcloud run services update blueberrymart-api --region=us-central1 --min-instances=0 --max-instances=3

# blueberrymart-worker: min 1 / max 1 (always-on consumer — original)
gcloud run services update blueberrymart-worker --region=us-central1 --min-instances=1 --max-instances=1
```

Cloud SQL takes a couple of minutes to come back up; Cloud Run services will start serving as
soon as a request arrives (or immediately for the worker, since it has `min-instances=1`).

---

## Cost of this guard itself

Negligible — Pub/Sub and a rarely-invoked Cloud Function (2nd gen) both have generous free
tiers; this budget notification fires at most a handful of times per day even while over
budget, well within them.
