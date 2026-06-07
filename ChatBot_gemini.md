# Customer Support Assistant (Chatbot) — Gemini

An in-app AI assistant that helps **customers** with two things only: (1) questions
about Blueberry Mart **items** (price, stock, which branch, bulk) and (2) **order /
support** issues (paying, pickup vs delivery, membership, loyalty, reviews). It politely
declines anything off-topic.

It runs on **Google Gemini's free tier** by default, but the backend speaks the generic
**OpenAI-compatible** chat format, so you can swap to Groq / OpenRouter / Ollama / OpenAI
by changing config only.

---

## How it answers questions about our items (no RAG, no fine-tuning)

The bot is grounded by **context injection**, not by training:

1. On every message, the backend runs a SQL query against the **live Postgres** catalog
   (`inventory` + `branches`).
2. It formats that into a snapshot (every item: price, stock, branch, bulk flag) and puts
   it — plus the app's rules (eSewa, delivery, Plus, loyalty, notify-me) — into the
   **system prompt**.
3. Gemini answers from that injected, always-current context.

Why not the heavier approaches:
- **Fine-tuning** bakes in static knowledge that goes stale instantly (stock changes) — wrong tool.
- **RAG (embeddings + vector DB)** is for when there's too much data to fit in a prompt.
  Our catalog is tiny (a few hundred tokens), so "retrieval" is just a `SELECT`. This *is*
  RAG with a trivial retrieval step — no vector DB needed.
- If the catalog ever grew to thousands of items / long policy docs, graduate to real RAG
  (e.g. **pgvector** in the existing Postgres) and inject only the top-k relevant chunks.

Current limitation: it does **not** see a specific user's orders ("where's my order
#1042?"). Add per-user order injection or a lookup tool later if wanted.

---

## Architecture / files

- `BlueberryMart.Api/Configuration/ChatOptions.cs` — `ApiKey`, `BaseUrl`, `Model`, `MaxTokens`
  (bound from the `"Chat"` section).
- `BlueberryMart.Api/Services/LlmChatService.cs` — calls an OpenAI-compatible
  `/chat/completions` endpoint; builds the scoped system prompt + live catalog.
  `DisabledChatService` is used when no key is set (`enabled:false`).
- `BlueberryMart.Api/Controllers/ChatController.cs` — `POST /api/chat`
  (`Customer,Shareholder`). Guardrails: caps history to 20 turns / 2000 chars, requires the
  last turn to be the user's. Returns `{ enabled, reply }`.
- `BlueberryMart.Api/Program.cs` — opt-in: real `LlmChatService` (typed HttpClient) when
  `Chat:ApiKey` is set, else `DisabledChatService`.
- Frontend: `src/services/chatService.ts`, `src/screens/tabs/ChatScreen.tsx`, and the
  **Assistant** tab in `CustomerTabs.tsx`.

The key lives **only server-side** (the app calls our `/api/chat`, never Gemini directly),
so it's never shipped to phones.

---

## Config (the `"Chat"` section)

| Key | Default (Gemini) | Notes |
|---|---|---|
| `ApiKey` | — (required to enable) | Gemini key from AI Studio, starts with `AIza` |
| `BaseUrl` | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` | OpenAI-compatible endpoint |
| `Model` | `gemini-2.0-flash` | or `gemini-2.5-flash` |
| `MaxTokens` | `600` | reply length cap |

Other providers (change BaseUrl + Model + ApiKey): Groq
(`https://api.groq.com/openai/v1/chat/completions`, `llama-3.3-70b-versatile`),
OpenRouter (`.../api/v1/chat/completions`, `…:free`), Ollama
(`http://localhost:11434/v1/chat/completions`, `llama3.2`).

---

## Get a free Gemini API key

1. Go to **https://aistudio.google.com/apikey**, sign in.
2. **Create API key** — let it auto-create a project or pick an existing GCP project.
3. Copy the key (starts with `AIza…`). Free tier; no billing required.

---

## Enable it

**Local dev** — `BlueberryMart.Api/appsettings.Development.json` (gitignored):
```json
"Chat": { "ApiKey": "AIza..." }
```

**Production (Cloud Run)** — store as a Secret Manager secret and reference it. The Cloud
Run runtime SA already has `secretmanager.secretAccessor`, and `--update-env-vars` deploys
don't wipe secret bindings.

First time (secret doesn't exist yet):
```bash
printf %s "AIzaYOURKEY" | gcloud secrets create gemini-api-key \
  --data-file=- --project=project-76ca6efe-7878-4dc8-bff
```
If the secret already exists, add a **new version** instead (do NOT use `create`):
```bash
printf %s "AIzaYOURKEY" | gcloud secrets versions add gemini-api-key \
  --data-file=- --project=project-76ca6efe-7878-4dc8-bff
```
Then point Cloud Run at it (creates a new revision that reads `:latest`):
```bash
gcloud run services update blueberrymart-api --region us-central1 \
  --project project-76ca6efe-7878-4dc8-bff \
  --update-secrets Chat__ApiKey=gemini-api-key:latest
```

> Your Expo app points at **production**, so set the **prod** secret to use the assistant
> on your phone. Set the local config only if you also run the backend locally.

---

## Verify

```bash
PROD=https://blueberrymart-api-278293545480.us-central1.run.app
TOKEN=$(curl -s -X POST $PROD/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"shareholder1@blueberrymart.com","password":"shareholder1_password"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")
curl -s -X POST $PROD/api/chat -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"Is Brown Eggs in stock and how much?"}]}'
```
Expect `{"enabled":true,"reply":"…"}` with a catalog-grounded answer.

---

## Troubleshooting

- **`enabled:false`** → `Chat:ApiKey` isn't set in that environment.
- **`502 "temporarily unavailable"`** → key is present but the Gemini call failed:
  - wrong/invalid key (must start with `AIza`; a Gemini key, not an OAuth token),
  - wrong `Model` for the endpoint,
  - secret holds the wrong value (remember `create` fails if the secret already exists —
    use `versions add`, then redeploy so `:latest` is re-read).
- **Key hygiene:** never paste keys in chat/commits/the frontend. If one leaks, delete it
  in AI Studio and add a new secret version; optionally destroy the old version:
  `gcloud secrets versions destroy <N> --secret=gemini-api-key --project=…`.
