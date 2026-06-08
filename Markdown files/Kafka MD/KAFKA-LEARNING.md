# Kafka & Event-Driven Architecture — A Learning Walkthrough

A concept-first tour of what we built into Blueberry Mart and *why*. Light on code,
heavy on the ideas. For the file map and run instructions see `KAFKA.md`; this doc is
the mental model.

---

## 1. The question that started it

> "Let me check if an item is in stock at a branch — like Sephora."

The first lesson came before any Kafka: **that feature is a database read.** "Is item
X in stock at branch Y *right now*?" is a fast, point-in-time lookup — exactly what
**Postgres** is for. Bolting Kafka onto a read would teach an anti-pattern.

Kafka earns its place on the *other* half of the idea: reacting to **changes** —
"tell me when it's back in stock", "feed analytics". Those are **events**, and events
are what Kafka is for.

**Takeaway:** pick the tool by the shape of the problem.
- A **question about now** → database (OLTP).
- A **reaction to something that happened** → event stream (Kafka).
- A **question about history/trends** → warehouse (OLAP / BigQuery).

---

## 2. State vs. events

Most apps store **state**: "eggs: 32 in stock." An event-driven app *also* records the
**change**: "eggs went 35 → 33 because an order was placed."

State answers *what is*. Events answer *what happened* — and because they're a
**log** you can replay them, fan them out to many readers, and build new views later
without changing the thing that produced them. We emit one event type:
`stock-changed { item, branch, oldQty, newQty, reason, at }`.

---

## 3. The core vocabulary (with our examples)

- **Topic** — a durable, append-only, **ordered log** of events. Ours:
  `inventory.stock-changed`. Writing doesn't overwrite; it appends. Reading doesn't
  delete; many readers can read the same log.
- **Producer** — writes events. Our API produces on order placement and restock.
- **Consumer** — reads events and does something. We have two.
- **Partition + key** — a topic is split into partitions for parallelism. Events with
  the **same key** go to the **same partition**, so they stay **ordered**. We key by
  `branch:item`, so all changes to one item at one branch are processed in order
  (you never see "back in stock" before the sale that emptied it).
- **Offset** — each event's position in the log (0, 1, 2…). A consumer tracks "I've
  processed up to offset N" by **committing** it. Restart → resume from there.
- **Consumer group** — a set of consumers that **share** a topic's partitions and a
  single committed position. Different groups are **independent** — each gets *every*
  event and tracks its *own* offset.

---

## 4. The two ideas that make it click

### Decoupling
The producer doesn't know who's listening. The restock endpoint just appends an event
and returns *immediately* — it has no idea that a notification will be sent or a
warehouse row written. Add a consumer tomorrow and the producer doesn't change.
Contrast with calling a `NotificationService` directly: the producer would depend on
it, wait for it, and break if it's down.

### Fan-out (the payoff)
We run **two consumer groups** on the same topic:
- `blueberrymart-backinstock` → creates "back in stock" notifications (operational).
- `blueberrymart-bigquery-sink` → streams events to BigQuery (analytics).

They read the same log, at their own pace, with their own offsets. One slow/broken
consumer doesn't affect the other. *That* is Kafka's superpower — one source of
truth, many independent reactions.

```
order / restock ──> inventory.stock-changed (the log)
                          │
        ┌─────────────────┴───────────────────┐
   group A: back-in-stock              group B: BigQuery sink
   (Postgres notifications)            (analytics warehouse)
```

---

## 5. Delivery guarantees (why we commit manually)

Our consumers **commit the offset only after** they've done their work
(`EnableAutoCommit=false`). So if a consumer crashes mid-processing, the offset isn't
committed and the event is **redelivered** → **at-least-once** delivery. The cost: a
message can be processed twice, so handlers should be **idempotent** (e.g. we mark a
subscription "notified" so a redelivery doesn't double-notify).

The producer side is weaker today: we publish *after* the DB commit, so a crash in
that gap loses the event. The production fix is the **transactional outbox** (write
the event into the DB in the same transaction, relay it to Kafka separately) — see
`KAFKA.md`.

---

## 6. Three databases, three jobs

| Layer | Question | Tool | Latency |
|---|---|---|---|
| Operational (OLTP) | "in stock now?" | **Postgres** | milliseconds, per-request |
| Event backbone | "what changed?" | **Kafka** | streaming |
| Analytical (OLAP) | "stock-out trends?" | **BigQuery** | seconds, big aggregates |

The same event feeds both an operational consumer (notifications) and an analytical
sink (BigQuery). Asking "is it in stock now?" of BigQuery would be slow and costly;
asking "net stock change by reason over months" of Postgres would be awkward. Right
tool, right question.

---

## 7. What we built, as concepts

1. **Stage 1 — producer + topic.** Saw events append to the log and inspected them
   (partitions, keys, offsets) in the Redpanda console.
2. **Stage 2 — a consumer + consumer group.** Turned the stream into a real feature
   (back-in-stock), learning offset commits and at-least-once delivery.
3. **Stage 3 — a second consumer group + a sink.** Fan-out to BigQuery, then analytics
   over the event history — OLAP vs OLTP made concrete.

---

## 8. Honest trade-offs (when *not* to reach for Kafka)

This is **overkill** for an app this size, and that's fine — the goal was learning.
In reality you'd add Kafka when you have: multiple services needing the same events,
real-time pipelines, high write throughput, or a need to replay history. For a single
app, an in-process event bus or a simple `outbox` table + cron often does the job with
a fraction of the operational weight. Kafka brings real cost: a broker to run,
consumers to keep alive, schemas to manage, lag to monitor.

---

## 9. What "production" actually requires

Running this for real is its own project (full checklist in `KAFKA.md`). Conceptually:

- **A broker you don't babysit** — managed Kafka (Confluent / Redpanda / GCP), with
  authentication (SASL/TLS) the local dev broker doesn't need.
- **Always-on consumers** — a consumer must run continuously. Our API host (Cloud Run)
  scales to zero, so consumers move to a dedicated always-on worker.
- **Stronger delivery** — a transactional outbox so an event is never lost between the
  DB commit and the publish.
- **Operability** — schema registry (vs raw JSON), consumer-lag monitoring, a
  dead-letter topic for poison messages, and retention/partition/cost sizing.
- **Permissions** — the worker's service account needs BigQuery insert + query roles.

The design is already production-shaped: everything is **opt-in** (no broker
configured ⇒ a no-op producer and the consumers/sink simply don't run), so production
stays untouched until these pieces are deliberately added.
