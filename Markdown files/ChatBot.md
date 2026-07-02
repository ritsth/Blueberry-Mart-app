# Customer Support Assistant (Chatbot)

An in-app AI assistant for **customers**, scoped to two things only: (1) questions about
Blueberry Mart **items** (price, stock, branch, bulk) and (2) **order/support** issues
(paying, pickup vs delivery, membership, loyalty, reviews). It politely declines anything
off-topic.

> **Status — LIVE in prod.** Cloud Run `blueberrymart-api` has `Chat__ApiKey` wired to the
> `groq-api-key` Secret Manager secret (plus `Chat__BaseUrl`/`Chat__Model` for Groq), so the
> assistant is enabled in production today — including the **back-in-stock subscription**:
> a customer can open the **Assistant** tab and say *"notify me when Organic Spinach is back in
> stock,"* and the `subscribe_back_in_stock` tool creates the same `StockSubscription` the
> "Notify me" button does (fulfilled into an Alerts notification on restock). The
> "Enable it" section below is the reference for **how** it was wired, not an outstanding TODO.

**Current provider: Groq (free tier)** — model `llama-3.3-70b-versatile`. The backend
speaks the generic **OpenAI-compatible** chat format, so any provider can be swapped in via
config (no code change): Groq, Google Gemini, OpenRouter, a local Ollama, OpenAI, …

> **Why not Gemini?** We tried it first but Gemini's API returns **`free_tier … limit: 0`**
> for this project — even with a fresh, valid `AIzaSy…` key minted directly in the
> billing-enabled project (`project-76ca6efe-…`, `billingAccounts/01F9E2-…`). Gemini's
> **paid tier doesn't activate** here (region / trial-credit billing), so there's simply no
> quota to spend. Switched to **Groq**, which is genuinely free and works in-region.

---

## How it answers about our items (no RAG, no fine-tuning)

Grounded by **context injection**, not training:
1. On every message the backend queries the **live Postgres** catalog (`inventory` + `branches`).
2. It formats a snapshot (each item: price, stock, branch, bulk flag) and puts it — plus the
   app's rules (eSewa, delivery, Plus, loyalty, notify-me) — into the **system prompt**.
3. The model answers from that injected, always-current context.

Why not the heavier approaches:
- **Fine-tuning** bakes in static knowledge that goes stale instantly (stock changes) — wrong tool.
- **RAG (embeddings + vector DB)** is for when there's too much data to fit in a prompt. Our
  catalog is tiny (a few hundred tokens), so "retrieval" is just a `SELECT`. This *is* RAG with
  a trivial retrieval step — no vector DB needed. If the catalog ever grew huge, graduate to
  real RAG (e.g. **pgvector** in the existing Postgres) and inject only the top-k chunks.

For **per-customer data and actions**, the assistant uses **tool-calling** (not injection).
The model can call these functions on demand:
- `get_order(order_number)` — *(read)* one order's status, items, total, branch, date.
- `list_my_orders()` — *(read)* the customer's recent orders.
- `subscribe_back_in_stock(item_name, branch?)` — *(action)* sign the customer up for a
  back-in-stock alert on an out-of-stock item (creates a `StockSubscription`).

`LlmChatService` runs a **tool loop**: send messages + tool definitions → if the model
returns `tool_calls`, execute them, append the results as `role:"tool"` messages, and call
again until it produces a final answer (capped at 5 rounds). Tools execute **scoped strictly
to the authenticated user id** (from the JWT in `ChatController`), so a customer only ever
sees their own orders; an unknown number → "not on your account." (Verified live: asking
about another customer's order is refused.) This scales to large histories — orders are
fetched only when needed, not stuffed into every prompt. (Requires a provider with
tool-calling support; Groq's `llama-3.3-70b-versatile` has it.)

---

## Architecture / files

- `BlueberryMart.Api/Configuration/ChatOptions.cs` — `ApiKey`, `BaseUrl`, `Model`, `MaxTokens`
  (bound from the `"Chat"` section). **Defaults to Groq.**
- `BlueberryMart.Api/Services/LlmChatService.cs` — calls an OpenAI-compatible
  `/chat/completions` endpoint; builds the scoped system prompt + live catalog, and runs the
  **tool loop** for the `get_order` / `list_my_orders` / `subscribe_back_in_stock` functions (executed scoped to the user).
  `DisabledChatService` is used when no key is set (`enabled:false`).
- `BlueberryMart.Api/Controllers/ChatController.cs` — `POST /api/chat`
  (`Customer,Shareholder`). Caps history to 20 turns / 2000 chars, requires the last turn to
  be the user's. Returns `{ enabled, reply }`.
- `BlueberryMart.Api/Program.cs` — opt-in: real `LlmChatService` (typed HttpClient) when
  `Chat:ApiKey` is set, else `DisabledChatService`.
- Frontend: `src/services/chatService.ts`, `src/screens/tabs/ChatScreen.tsx`, **Assistant** tab
  in `CustomerTabs.tsx`.

The key lives **only server-side** (the app calls our `/api/chat`, never the LLM directly).

---

## Config (the `"Chat"` section)

| Key | Default (Groq) | Notes |
|---|---|---|
| `ApiKey` | — (required to enable) | Groq key, starts with `gsk_` |
| `BaseUrl` | `https://api.groq.com/openai/v1/chat/completions` | OpenAI-compatible endpoint |
| `Model` | `llama-3.3-70b-versatile` | Groq free model |
| `MaxTokens` | `600` | reply length cap |

Switch providers (set BaseUrl + Model + ApiKey):
- **Groq** (current): `https://api.groq.com/openai/v1/chat/completions`, `llama-3.3-70b-versatile`
- **Gemini** (needs paid tier — unavailable free in some regions): `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions`, `gemini-2.0-flash`
- **OpenRouter**: `https://openrouter.ai/api/v1/chat/completions`, `…:free`
- **Ollama** (local, free): `http://localhost:11434/v1/chat/completions`, `llama3.2`

---

## Get a free Groq key

1. **console.groq.com** → sign in → **API Keys** → **Create API Key** (no card).
2. Copy it (starts with `gsk_`).

---

## Enable it (already done in prod — reference)

**Local dev** — `BlueberryMart.Api/appsettings.Development.json` (gitignored). Base URL/model
default to Groq, so only the key is needed:
```json
"Chat": { "ApiKey": "gsk_..." }
```

**Production (Cloud Run)** — store the key in Secret Manager, then point the service at it.
The runtime SA already has `secretmanager.secretAccessor`.
```bash
# 1) store the key (run yourself; never paste the key in chat)
printf %s "gsk_YOURKEY" | gcloud secrets create groq-api-key \
  --data-file=- --project=project-76ca6efe-7878-4dc8-bff
#    (if it already exists, use: gcloud secrets versions add groq-api-key --data-file=- …)

# 2) wire it + (belt-and-suspenders) the Groq endpoint/model, and redeploy
gcloud run services update blueberrymart-api --region us-central1 --project project-76ca6efe-7878-4dc8-bff \
  --update-env-vars "Chat__BaseUrl=https://api.groq.com/openai/v1/chat/completions,Chat__Model=llama-3.3-70b-versatile" \
  --update-secrets Chat__ApiKey=groq-api-key:latest
```
> The app code already defaults to Groq, so the `--update-env-vars` above is optional belt-and-
> suspenders; the only thing strictly required in prod is the `Chat__ApiKey` secret.

---

## Verify (this is live and working)

Log in with a real shareholder account (Production no longer seeds demo login accounts — see
`SECURITY_POSTURE.md`), then hit `/api/chat` with that token:

```bash
PROD=https://blueberrymart-api-278293545480.us-central1.run.app
TOKEN=$(curl -s -X POST $PROD/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<your-shareholder-email>","password":"<your-password>"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")
curl -s -X POST $PROD/api/chat -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"Is Brown Eggs in stock and how much?"}]}'
```
Confirmed reply (2026-06): *"Yes, Brown Eggs (12 pack) are in stock at Blueberry Mart Downtown,
with 41 packs available, and they cost Rs 180."* — grounded in the live catalog.

For local/dev testing, the demo `shareholder1@blueberrymart.com` account is still seeded
automatically (Development/Testing only).

---

## Troubleshooting

- **`enabled:false`** → `Chat:ApiKey` isn't set in that environment.
- **`502 "temporarily unavailable"`** → key present but the provider call failed:
  - wrong/invalid key,
  - wrong `Model` for the chosen `BaseUrl`,
  - provider quota exhausted (e.g. Gemini's `free_tier limit: 0` in unsupported regions),
  - secret holds the wrong value — remember `gcloud secrets create` fails if the secret exists;
    use `versions add`, then redeploy so `:latest` is re-read.
- **Groq free-tier quirks (and how we handle them):**
  - `400 tool_use_failed` — Groq's Llama intermittently emits a malformed tool call. `LlmChatService`
    retries up to 2× on this. (This is also why **items are injected, not a tool** — the common path
    needs no tool call. A `search_items` tool was tried and removed because the model kept botching it.)
  - `429 rate_limit_exceeded` — the free tier is ~12k tokens/min; bursts (or many tool rounds) can hit
    it, surfacing as the generic 502. Fine for light use; for heavier traffic switch to a paid tier or a
    higher-limit model.
- **Key hygiene:** never paste keys in chat/commits/the frontend. To rotate: create a new key,
  add a new secret version, redeploy; destroy the old version with
  `gcloud secrets versions destroy <N> --secret=<name> --project=…`.
