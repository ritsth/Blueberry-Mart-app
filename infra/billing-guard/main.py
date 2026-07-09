"""
Cloud Function (2nd gen, Pub/Sub-triggered) that reacts to Cloud Billing budget
notifications. When actual spend reaches TRIGGER_RATIO of the budget, it:

  1. Scales blueberrymart-api and blueberrymart-worker (Cloud Run) to 0/0
     min/max instances — stops new chat/email/BigQuery-triggered requests
     within about a minute.
  2. Stops the Cloud SQL instance (activation policy NEVER) — the single
     biggest fixed cost, since it bills 24/7 regardless of traffic.

Both actions are reversible in seconds (see infra/billing-guard/README.md,
"Resume service"). No data is deleted; Cloud SQL storage/backups are
unaffected by activation policy.

Budget notifications are re-sent periodically with the current cost — not
only when a threshold is newly crossed — so every handler run re-checks
whether action is still needed (idempotent) instead of blindly re-patching.
"""

import base64
import json
import logging
import os

from google.cloud import run_v2
from googleapiclient.discovery import build

PROJECT_ID = os.environ["PROJECT_ID"]
REGION = os.environ["REGION"]
CLOUD_RUN_SERVICES = [s.strip() for s in os.environ["CLOUD_RUN_SERVICES"].split(",") if s.strip()]
SQL_INSTANCE = os.environ["SQL_INSTANCE"]
TRIGGER_RATIO = float(os.environ.get("TRIGGER_RATIO", "1.0"))
# Optional: only act on budget notifications for this exact budget display name.
# Leave unset to act on any budget notification delivered to this topic.
BUDGET_DISPLAY_NAME = os.environ.get("BUDGET_DISPLAY_NAME", "")

logging.basicConfig(level=logging.INFO)


def budget_alert(cloud_event):
    """Entry point. Triggered by the budget's Pub/Sub topic via Eventarc."""
    payload = json.loads(base64.b64decode(cloud_event.data["message"]["data"]).decode("utf-8"))

    display_name = payload.get("budgetDisplayName", "")
    cost = float(payload.get("costAmount", 0))
    budget = float(payload.get("budgetAmount", 0))

    if BUDGET_DISPLAY_NAME and display_name != BUDGET_DISPLAY_NAME:
        logging.info("Ignoring notification for budget %r (watching %r).", display_name, BUDGET_DISPLAY_NAME)
        return

    if budget <= 0:
        logging.warning("Budget amount is 0 or missing in notification payload; skipping.")
        return

    ratio = cost / budget
    logging.info("Budget %r: spend %.2f / %.2f (%.0f%%).", display_name, cost, budget, ratio * 100)

    if ratio < TRIGGER_RATIO:
        logging.info("Below trigger ratio (%.0f%%) — no action.", TRIGGER_RATIO * 100)
        return

    logging.warning(
        "Spend has reached %.0f%% of budget (>= trigger %.0f%%). Stopping billable resources.",
        ratio * 100, TRIGGER_RATIO * 100,
    )
    _stop_cloud_run_services()
    _stop_cloud_sql()


def _stop_cloud_run_services():
    client = run_v2.ServicesClient()
    for service_name in CLOUD_RUN_SERVICES:
        full_name = f"projects/{PROJECT_ID}/locations/{REGION}/services/{service_name}"
        try:
            service = client.get_service(name=full_name)
        except Exception:
            logging.exception("Could not read Cloud Run service %s; skipping.", service_name)
            continue

        scaling = service.template.scaling
        if scaling.min_instance_count == 0 and scaling.max_instance_count == 0:
            logging.info("%s is already scaled to 0/0 — skipping.", service_name)
            continue

        scaling.min_instance_count = 0
        scaling.max_instance_count = 0
        try:
            client.update_service(service=service)
            logging.warning("Scaled %s to 0/0 instances.", service_name)
        except Exception:
            logging.exception("Failed to scale %s to 0/0.", service_name)


def _stop_cloud_sql():
    sqladmin = build("sqladmin", "v1beta4", cache_discovery=False)
    try:
        instance = sqladmin.instances().get(project=PROJECT_ID, instance=SQL_INSTANCE).execute()
    except Exception:
        logging.exception("Could not read Cloud SQL instance %s; skipping.", SQL_INSTANCE)
        return

    if instance.get("settings", {}).get("activationPolicy") == "NEVER":
        logging.info("Cloud SQL instance %s is already stopped — skipping.", SQL_INSTANCE)
        return

    try:
        sqladmin.instances().patch(
            project=PROJECT_ID,
            instance=SQL_INSTANCE,
            body={"settings": {"activationPolicy": "NEVER"}},
        ).execute()
        logging.warning("Stopped Cloud SQL instance %s (activationPolicy=NEVER).", SQL_INSTANCE)
    except Exception:
        logging.exception("Failed to stop Cloud SQL instance %s.", SQL_INSTANCE)
