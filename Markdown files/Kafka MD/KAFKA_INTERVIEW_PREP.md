# Kafka — Interview Prep (grounded in Blueberry Mart)

Study notes for talking about Kafka from *your own* code, not abstract definitions. Each
concept is anchored with **"in your project"** so you recall something you actually built.
The pipeline itself is documented in `KAFKA_LOCAL.md` (concepts + local) and
`KAFKA_PRODUCTION.md` (Confluent Cloud + Cloud Run worker).

## The 30-second pitch (lead with this)

> "I built an event-driven inventory pipeline. When stock changes — an order, a restock, an
> adjustment — the API publishes a `stock-changed` event to Kafka. Two independent consumers
> react: one creates 'back-in-stock' notifications, the other streams events into BigQuery for
> analytics. The point was **fan-out** — one event stream, many consumers — and decoupling the
> producer from whoever reacts."

---

## Concepts, anchored to your code

**1. Topic — durable, append-only, ordered log.**
*In your project:* `inventory.stock-changed`. Writes append; reads don't delete, so multiple
consumers read the same log independently. (Contrast a queue like RabbitMQ: consume-and-delete,
one logical consumer. Kafka **retains** the log.)

**2. Partition + key → ordering.**
A topic splits into partitions for parallelism. **Same key → same partition → ordered within
that key.** *In your project:* you key by `branch:item`. **Why it matters:** all changes to one
item at one branch land in the same partition and are processed in order — you never see "back
in stock" before the sale that emptied it. Kafka only guarantees order **within a partition**,
not across the topic, so the key scopes ordering to what matters.

**3. Offset — a consumer's position in the log.**
Each event has an offset (0,1,2…). A consumer **commits** "processed up to N"; on restart it
resumes there. *In your project:* `AutoOffsetReset.Earliest` (a fresh group reads from the start).

**4. Producer.**
*In your project:* `KafkaStockEventProducer` — one shared, thread-safe producer for the app's
life. `Produce()` is non-blocking (enqueues, returns immediately; a delivery-report callback
logs failures). `Flush()` on dispose so buffered events aren't lost on shutdown.

**5. Consumer group.**
Consumers in a group **share** a topic's partitions and one committed position. **Different
groups are independent** — each gets every event with its own offset. *In your project:* two
groups, `blueberrymart-backinstock` and `blueberrymart-bigquery-sink` → **fan-out**: a slow or
broken consumer doesn't affect the other.

**6. Delivery semantics — the one they'll push on.**
- *at-most-once*: commit **before** processing → crash = lost message.
- *at-least-once*: commit **after** processing → crash = redelivery (possible duplicate).
- *exactly-once*: hardest; needs transactions/idempotency.

*In your project:* `EnableAutoCommit=false`, commit **after** the work → **at-least-once**.
Because duplicates are possible, the handler is **idempotent**: you mark a subscription
`NotifiedAt`, so a redelivered event can't double-notify. (Say the idempotency part unprompted.)

**7. Dual-write / outbox problem — your honest weak spot.**
*In your project:* you publish **after** the DB commit, fire-and-forget — so a crash between the
commit and the publish loses that event. **Say proactively:** "It's at-least-once on the
consumer side but lossy on the producer side because of the dual-write problem. The fix is a
**transactional outbox** — write the event into an `outbox` table in the *same* DB transaction,
and a relay publishes it to Kafka. I scoped it as a known follow-up." Naming the problem *and*
the fix reads as senior.

**8. Opt-in / no-op design.**
*In your project:* no broker configured → no-op producer, consumers don't register. **Why:** keeps
Kafka entirely optional (prod and tests run without a broker) and decouples app startup from the
broker being up.

---

## "Why" questions (the ones that separate you from a memorizer)

- **Why Kafka, not call the notification service directly?** Decoupling — the restock endpoint
  appends an event and returns; it doesn't know or wait for notifications/analytics, and adding a
  third consumer needs zero producer changes.
- **Why Kafka, not RabbitMQ / a queue?** Kafka retains the log and supports multiple independent
  consumer groups **replaying the same stream** (fan-out). A queue is consume-and-delete.
- **Why not just query Postgres for analytics?** Right tool per question: "in stock *now*?" =
  fast OLTP read (Postgres); "stock-out *trends*?" = big OLAP aggregate (BigQuery). Kafka is the
  pipe feeding the warehouse.
- **⭐ When would you NOT use Kafka?** "For an app this size it's overkill — a single service with
  an in-process event bus or an `outbox` table + cron does the job with far less operational
  weight. You reach for Kafka with multiple services needing the same events, real-time pipelines,
  high write throughput, or replay needs." Knowing when *not* to use it is the strongest signal.

---

## Be ready to discuss (didn't build, but speak to)

- **Rebalancing** — partitions reassign when a consumer joins/leaves a group (you ran one
  consumer per group, so you didn't hit it).
- **Consumer lag** — how far behind a consumer is; what you'd monitor in prod.
- **Dead-letter queue** — where poison messages go so they don't block the partition. (Your
  handler currently retries forever on failure — a real gap to mention.)
- **Schema registry (Avro/Protobuf)** — you used raw JSON; a registry enforces schema evolution.
  "JSON for simplicity; I'd add a registry for a multi-team prod system."
- **Partition count / retention** — tuning knobs you'd size by throughput.

---

## Likely questions → your answers

1. *Walk me through a project where you used Kafka.* → the 30-second pitch, then fan-out.
2. *How do you guarantee ordering?* → key by `branch:item`; order is per-partition.
3. *At-least-once or exactly-once?* → at-least-once (manual commit after processing) + idempotent
   handler (`NotifiedAt`).
4. *Consumer crashes mid-message?* → offset uncommitted → redelivered → idempotency prevents
   double effects.
5. *Crash after the DB write but before publishing?* → dual-write problem; fix = transactional
   outbox.
6. *Two consumers, same data — how?* → two consumer groups, independent offsets.
7. *Would you use Kafka for this in real life?* → honest "it's overkill, here's when I would."

---

## The closer

The most convincing add-on: **"and I deployed it to production on Confluent Cloud with a
dedicated Cloud Run consumer worker"** — see `KAFKA_PRODUCTION.md`. "Ran it locally" vs.
"deployed it to prod with SASL auth and an always-on worker" are very different sentences.
