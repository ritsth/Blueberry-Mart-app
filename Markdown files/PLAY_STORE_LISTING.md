# Google Play — Store Listing (Blueberry Mart)

Copy-paste-ready text for the Play Console **Store listing** page, plus the other fields
you'll be asked to fill. Character limits are Google's; everything below is within limit.

---

## App name  *(max 30 chars)*

```
Blueberry Mart
```

---

## Short description  *(max 80 chars — shows in search results)*

**Recommended:**
```
Fresh groceries & blueberries delivered. Shop, bulk-order & track it all in-app.
```

Alternatives (pick one):
```
Shop fresh groceries & blueberries, order in bulk, get it delivered fast.
```
```
Your neighborhood mart in your pocket — shop, bulk-order & get it delivered.
```

---

## Full description  *(max 4000 chars)*

```
Blueberry Mart brings your neighborhood store to your phone. Browse fresh produce, everyday groceries, and our signature blueberries, then check out in seconds and have it delivered to your door.

Whether you're grabbing a few items or stocking up for a business, Blueberry Mart makes it simple.

WHAT YOU CAN DO

• Shop fresh — Browse products by category with photos, prices, and live availability.
• Explore & discover — Find new items, featured picks, and deals on the Explore tab.
• Bulk ordering — Need larger quantities? Order in bulk with pricing built for volume.
• Smart cart — Add, adjust, and review your order before you check out.
• Fast delivery — Save multiple delivery addresses and get your order brought to you.
• Track your orders — Follow every order from confirmation to your doorstep in Activity.
• In-app assistant — Ask our built-in assistant for help finding products or placing an order.
• Ratings & reviews — See what other shoppers think, and leave your own reviews.
• Alerts & notifications — Stay updated on order status, restocks, and offers.
• Secure checkout — Pay easily and safely with eSewa.

WHY BLUEBERRY MART

• Real-time stock so you only order what's actually available.
• Built for both quick personal shopping and larger bulk orders.
• A clean, fast, easy-to-use experience from browse to checkout.
• Order history and saved addresses make reordering effortless.

Download Blueberry Mart and get fresh groceries and blueberries delivered — the easy way to shop your local mart.
```

> Trim the assistant / eSewa lines if any feature isn't live in this release — the listing must
> match what the app actually does (Google reviews for this).

---

## Other Store-listing fields

| Field | Suggested value |
|-------|-----------------|
| **App category** | Shopping |
| **Tags** | grocery, delivery, shopping, fresh produce |
| **Email** (public support) | akitirsth@gmail.com *(or a dedicated support address)* |
| **Website** | *(optional — your landing page / Firebase site if you have one)* |
| **Phone** | *(optional)* |

---

## Graphics you must upload (Google requires these)

| Asset | Spec | Notes |
|-------|------|-------|
| **App icon** | 512×512 PNG | The blueberry icon — export from `assets/icon.png` (it's 1024; resize to 512). |
| **Feature graphic** | 1024×500 PNG/JPG | Banner at top of your listing. Needs to be made — I can generate a simple branded one. |
| **Phone screenshots** | 2–8 images, min 320px side | Capture from the emulator: Shop, Cart, Activity, etc. (see below). |

### Capturing screenshots from the emulator
With the app running on the emulator:
```bash
~/Library/Android/sdk/platform-tools/adb exec-out screencap -p > ~/Desktop/shot1.png
```
Navigate to a screen, run the command, repeat. Grab 3–5 nice screens (Shop, product, Cart, Activity).

---

## Content rating
You'll fill a questionnaire. For a shopping app with no violence/gambling/mature content, this
comes out **Everyone / PEGI 3**. Answer honestly (does it have user-generated content via reviews? → yes).

## Data safety form
Declare what the app collects and sends to your backend. Based on the app, you likely collect:
- **Personal info:** name, email (account), delivery address.
- **Financial info:** handled via eSewa (payment processor) — declare if any payment data passes through.
- **App activity:** orders, in-app actions.
- State whether data is **encrypted in transit** (yes — HTTPS to the Cloud Run API) and whether
  users can **request deletion**.

## Privacy policy  *(required URL)*
Google requires a hosted privacy-policy URL. If you don't have one yet, I can draft a privacy
policy page you can host (e.g. on your Firebase site) — just ask.

---

*Tip: You can save the listing as a draft and keep editing. Nothing goes public until you submit
a release to the Production track (and, for personal accounts, after the 14-day closed test).*
