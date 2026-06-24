# Email & Domain Setup

## Domain: blueberrymart.shop

Registered on **Namecheap**. Used exclusively for email sending (the backend still runs on the
Cloud Run `…run.app` URL — the domain isn't mapped there).

---

## Resend (email provider)

- **Plan:** Free tier — 3,000 emails/month, 100/day
- **Dashboard:** https://resend.com/emails
- **Sending domain:** `blueberrymart.shop` (verified)
- **From address:** `noreply@blueberrymart.shop`
- **From name:** `Blueberry Mart`

### DNS records added on Namecheap

| Type  | Name / Host                             | Purpose          |
|-------|-----------------------------------------|------------------|
| TXT   | `@`                                     | SPF              |
| TXT   | `resend._domainkey`                     | DKIM             |
| MX    | `@` → `feedback-smtp.us-east-1.amazonses.com` | Bounce handling |
| TXT   | `_dmarc`                                | DMARC policy     |

All four records verified in Resend (green ✓). Confirmed via `dig` before clicking Verify.

---

## Where secrets live

| Secret | Location |
|--------|----------|
| `Email__ApiKey` (Resend API key) | Google Secret Manager → secret named `resend-api-key` (mounted into Cloud Run at deploy time) |
| `Email__FromAddress` | `deploy.yml` → `--update-env-vars` (`noreply@blueberrymart.shop`) |
| `Email__PublicBaseUrl` | `deploy.yml` → `--update-env-vars` (the prod `…run.app` URL) |

The API key is **never** in `deploy.yml`, git history, or chat — Secret Manager only.

---

## Local development

To send real emails locally, paste the Resend API key into the gitignored
`BlueberryMart.Api/appsettings.Development.json`:

```json
{
  "Email": {
    "ApiKey": "re_...",
    "FromAddress": "noreply@blueberrymart.shop",
    "PublicBaseUrl": "http://<your-lan-ip>:5027"
  }
}
```

Leave `ApiKey` empty (or omit it) and the app falls back to `LoggingEmailSender`, which prints the
email subject to the console instead of sending — useful for tests and offline dev.

---

## How the feature works (summary)

- **Register** → user gets a verification link emailed to them; they can't log in until they click it.
- **Verify link** → `GET /api/auth/verify-email?uid=…&t=…` → sets `email_verified = true` → shows branded success page in browser; app polls and auto-advances to sign-in.
- **Forgot password** → `POST /api/auth/forgot-password` → reset link emailed (always 200, no enumeration).
- **Reset page** → `https://…run.app/reset-password.html?uid=…&t=…` → hosted static page, POSTs new password to the API.
- **Existing accounts** were grandfathered `email_verified = true` in the migration (no disruption to testers).
- **Google sign-in** users are auto-verified (Google already vouches for the address).

Tokens are 32-byte random secrets, stored as PBKDF2 hashes. Single-use, 24 h expiry (verify) / 1 h (reset).
