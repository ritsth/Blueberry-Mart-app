# Google Sign-In — setup checklist

The code for "Continue with Google" is implemented (backend `POST /api/auth/google` + mobile
button). It stays inert until you complete this one-time Google Cloud setup and fill in the
**Web client ID** in two places. Until then the button shows but returns "Could not verify your
Google account."

## 1. Google Cloud — OAuth credentials
In the Google Cloud Console for your project (see `GCP_SERVICES.md` for the project ID):

1. **APIs & Services → OAuth consent screen**: External; app name **Blueberry Mart**; support email
   `akitirsth@gmail.com`; add scopes `email` and `profile` (both non-sensitive → no Google review).
2. **APIs & Services → Credentials → Create credentials → OAuth client ID**, twice:
   - **Web application** → name it "Blueberry Mart Web". Copy its **Client ID**
     (`…apps.googleusercontent.com`). This is the **Web client ID** used everywhere below.
   - **Android** → package name `com.ritsth.BlueberryMartApp` + the signing **SHA-1** (next step).

## 2. Get the SHA-1 fingerprint(s)
EAS manages the Android keystore, so:
```bash
cd BlueberryMartApp
eas credentials        # Android → production → shows the SHA-1 fingerprint
```
Register that SHA-1 on the **Android** OAuth client. **Also** add the **Play App Signing** SHA-1
(Play Console → your app → Setup → App signing) — Google re-signs the store build, so its on-device
fingerprint differs from the upload key. Add both so Sign-In works in internal *and* store builds.

## 3. Wire the Web client ID into the app + API
The Web client ID is **not secret** (it ships in the app bundle), so plain env vars are fine.

**Mobile** — in `BlueberryMartApp/eas.json`, replace the placeholder in **all three** build profiles
(`development`, `preview`, `production`):
```
"EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID": "<WEB_CLIENT_ID>.apps.googleusercontent.com"
```

**Backend** — set it on Cloud Run once (persists across deploys, since `deploy.yml` only *merges*
env vars):
```bash
gcloud run services update blueberrymart-api \
  --region us-central1 --project "$(gcloud config get-value project)" \
  --update-env-vars Google__WebClientId=<WEB_CLIENT_ID>.apps.googleusercontent.com
```
(For local dev, put `Google:WebClientId` in `appsettings.Development.json`.)

## 4. Build & test (not Expo Go)
Google Sign-In is a native module — it does **not** run in Expo Go. Use a dev build:
```bash
cd BlueberryMartApp
eas build --platform android --profile development
```
Install it on the emulator (our `Pixel_3a_API_32` AVD has Play Services; add a Google account to it
in Settings), then `npx expo start --dev-client`, open the app, tap **Continue with Google**.

## Notes / troubleshooting
- **`DEVELOPER_ERROR`** on tap = SHA-1 / package name / client-ID mismatch — recheck step 1–2.
- The backend validates the ID token's audience against `Google:WebClientId`, so the app's
  `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID` and the API's `Google__WebClientId` **must be the same value**.
- New Google users are auto-created as `customer`; an existing email is linked to its account.
- Apple Sign-In is intentionally deferred (only required by Apple on iOS once Google is offered there).
