# Android — Emulator & Google Play Shipping Guide

End-to-end guide for running the Blueberry Mart mobile app on the **Android emulator**
(no physical device needed) and **publishing it to the Google Play Store**.

The app lives in `BlueberryMartApp/` — a **managed Expo (SDK 54)** React Native app.
Because it's managed Expo, builds happen **in the cloud via EAS** — you do **not** need
Android Studio, a local JDK, or a Mac signing setup. You only need the Android **emulator**
(which is already installed on this machine) for testing.

---

## 0. Key facts / reference

| Thing | Value |
|-------|-------|
| App project | `BlueberryMartApp/` (Expo SDK 54.0.x, React Native 0.81) |
| Android package name | `com.ritsth.BlueberryMartApp` |
| iOS bundle id | `com.ritsth.BlueberryMartApp` |
| Expo account (owner) | `ritsth` |
| EAS project id | `3bbda575-f718-4332-b5b8-4edf68983ef3` |
| EAS project dashboard | https://expo.dev/accounts/ritsth/projects/BlueberryMartApp |
| Android signing keystore | Managed by EAS (cloud), credential id `dftHqek7To` — **never delete this** |
| Prod API the app calls | `https://blueberrymart-api-278293545480.us-central1.run.app` |
| Emulator (AVD) name | `Pixel_3a_API_32_arm64-v8a` |
| Android SDK location | `~/Library/Android/sdk` |

> ⚠️ **Signing key is forever.** Google Play permanently binds your app to the keystore
> used for the first upload. EAS stores it for you. Don't regenerate or lose it, or you can
> never update the app under the same listing.

---

## 1. One-time environment setup

The Android SDK is already installed at `~/Library/Android/sdk`, but its tools aren't on the
shell `PATH`. Add these to `~/.zshrc` (then `source ~/.zshrc`):

```bash
export ANDROID_HOME="$HOME/Library/Android/sdk"
export PATH="$PATH:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator"
```

Verify:

```bash
adb version
emulator -list-avds      # should list: Pixel_3a_API_32_arm64-v8a
eas --version            # eas-cli 20.x or newer (older 0.x will NOT work with SDK 54)
eas whoami               # should print: ritsth
```

If `eas` is old: `npm install -g eas-cli@latest`.

> You do **not** need Java or Android Studio. EAS builds in the cloud.

---

## 2. Run the app in the Android emulator (simulator)

### 2a. Launch the emulator

```bash
emulator -avd Pixel_3a_API_32_arm64-v8a
```

Leave it running in its own terminal. First cold boot takes ~30s. To check it's ready:

```bash
adb wait-for-device
adb shell getprop sys.boot_completed   # "1" means booted
adb devices                            # should show: emulator-5554  device
```

### 2b. Run the app on it (fast dev loop — Expo Go)

From `BlueberryMartApp/`:

```bash
cd BlueberryMartApp
npx expo start --android
```

This installs **Expo Go** on the emulator and opens the app inside it, with live reload.
It reads `.env.local` so it talks to the **prod API**. Best for day-to-day development.
(Press `a` in the Expo terminal to re-open on Android, `r` to reload.)

### 2c. Useful emulator commands

```bash
adb exec-out screencap -p > /tmp/shot.png    # screenshot the emulator
adb install -r path/to/app.apk               # install an APK
adb shell monkey -p com.ritsth.BlueberryMartApp -c android.intent.category.LAUNCHER 1   # launch app
adb emu kill                                 # shut the emulator down
```

### 2d. ⚠️ "INSUFFICIENT_STORAGE" when installing

The `Pixel_3a_API_32` AVD originally had only an **800 MB** data partition — too small for
Expo Go (~70 MB) plus the app. This was fixed by bumping the partition to **8 GB** permanently
in the AVD config:

`~/.android/avd/Pixel_3a_API_32_arm64-v8a.avd/config.ini`
```ini
disk.dataPartition.size=8192M
```

If you ever recreate the AVD and hit storage errors again, either set that line, or launch once with:
```bash
emulator -avd Pixel_3a_API_32_arm64-v8a -wipe-data -partition-size 8192
```
(`-wipe-data` erases the emulator's data and resizes the partition. Only needed once.)

> If the emulator window is closed, the device disappears (`adb devices` empty). Just relaunch it.

---

## 3. Build configuration (already set up)

### `BlueberryMartApp/app.json` (relevant bits)
```json
"android": {
  "package": "com.ritsth.BlueberryMartApp",
  "adaptiveIcon": { ... }
},
"extra": { "eas": { "projectId": "3bbda575-f718-4332-b5b8-4edf68983ef3" } },
"owner": "ritsth"
```

### `BlueberryMartApp/eas.json`
```json
{
  "cli": { "version": ">= 20.0.0", "appVersionSource": "remote" },
  "build": {
    "development": { "developmentClient": true, "distribution": "internal" },
    "preview": {
      "distribution": "internal",
      "android": { "buildType": "apk" },
      "env": { "EXPO_PUBLIC_API_URL": "https://blueberrymart-api-278293545480.us-central1.run.app" }
    },
    "production": {
      "autoIncrement": true,
      "env": { "EXPO_PUBLIC_API_URL": "https://blueberrymart-api-278293545480.us-central1.run.app" }
    }
  },
  "submit": { "production": {} }
}
```

**Why the `env` block:** `.env.local` is git-ignored, so EAS's cloud builds can't see it. The
prod API URL is baked into each build profile instead. (It's a public `EXPO_PUBLIC_*` value,
embedded in the client bundle anyway — not a secret.)

**Profiles:**
- `preview` → installable **APK** (sideload / emulator / share a link). Free.
- `production` → **AAB** (Android App Bundle) — the format Google Play requires.
- `appVersionSource: remote` + `autoIncrement` → EAS tracks `versionCode` on its server and
  bumps it every production build, so you never get "version code already used" on Play.

---

## 3b. App icon

The app icon is the **🫐 blueberry emoji** (the same one shown on the login screen,
`<Text>🫐</Text>` in `src/screens/LoginScreen.tsx`). It is rendered to PNGs by a small Swift
script (uses Apple Color Emoji), on the brand background `#E6F4FE`:

| File | Size | Notes |
|------|------|-------|
| `assets/icon.png` | 1024×1024 | iOS icon + fallback; blueberry on `#E6F4FE` |
| `assets/android-icon-foreground.png` | 512×512 | adaptive icon foreground; **transparent**, blueberry sized to stay inside the circular safe zone |
| `assets/android-icon-background.png` | 512×512 | adaptive background (solid light blue) — unchanged |
| `assets/favicon.png` | 48×48 | web |

The adaptive-icon `backgroundColor` (`#E6F4FE`) is set in `app.json` under `expo.android.adaptiveIcon`.

To regenerate (e.g. to change the emoji or background), edit and re-run the generator:
```bash
swift BlueberryMartApp/scripts/make_icon.swift   # writes the three PNGs into assets/
```
The icon is **baked in at build time** — it only appears in an installed APK/AAB, not in Expo Go.
After changing it you must rebuild (`eas build …`) to see it on a device.

> Note: `android-icon-monochrome.png` (used only for Android 13+ "themed icons") still shows the
> old logo silhouette — an emoji doesn't translate to a single-color silhouette. Low impact;
> regenerate separately if you want themed-icon consistency.

## 4. Building

> EAS builds use **committed** git state. Commit `app.json` / `eas.json` changes before building.
> (Committing locally is enough — you do **not** need to push.)

### Preview APK (for emulator / testers)
```bash
cd BlueberryMartApp
eas build --platform android --profile preview --non-interactive
```
Outputs a downloadable `.apk`. Get the direct URL later with:
```bash
eas build:view <BUILD_ID> --json   # .artifacts.buildUrl
```

### Production AAB (for Google Play)
```bash
cd BlueberryMartApp
eas build --platform android --profile production --non-interactive
```
Outputs a `.aab`, signed with the same EAS keystore.

The first build auto-generates the keystore in the cloud (no local Java needed). Every build
after reuses it. Builds run in EAS's free-tier queue (~10–25 min each).

### Install & test the APK on the emulator
```bash
curl -sL "<APK_URL>" -o /tmp/bbmart.apk
adb install -r /tmp/bbmart.apk
adb shell monkey -p com.ritsth.BlueberryMartApp -c android.intent.category.LAUNCHER 1
```
(You can't `adb install` an AAB — that's why `preview` builds an APK for testing.)

---

## 5. Publishing to Google Play

### 5a. Costs
- **Google Play: $25 one-time, ever.** (Compare: Apple App Store is $99/year.)
- EAS cloud builds: free tier is sufficient.
- Android emulator testing: free.

### 5b. Create the developer account
1. Go to https://play.google.com/console and sign in with the Google account that will own the app.
2. Pay the **one-time $25** registration fee.
3. Choose **Personal** or **Organization**.

> ⚠️ **Personal accounts created after Nov 2023** must run a **closed test with ≥12 testers
> for 14 continuous days** *before* they can apply for production (public) access. Plan for a
> ~2-week lead time. Organization accounts (with D-U-N-S verification) skip this.

### 5c. Create the app & store listing (one time)
In the console: **Create app** → name "Blueberry Mart", default language, app/game, free/paid,
accept declarations. Then complete:
- App icon, **screenshots** (capture from the emulator — at least a few phone screenshots),
  feature graphic, short + full description.
- **Privacy policy URL** (required).
- **Content rating** questionnaire.
- **Data safety** form (what data the app collects/sends).
- Target audience & content declarations.

### 5d. Upload the build (recommended order)
1. **Testing → Closed testing** → create a track → add ≥12 testers (emails or a Google Group).
2. **Create release** → upload the **`.aab`** → roll out. Let it run **14 days**.
3. Then **apply for production access**, and **Production → Create release** → promote the build.

### 5e. (Optional) Automate future uploads
After the first manual upload, you can push builds straight to Play from the CLI:
1. In Google Cloud / Play Console, create a **service account** with Play access and download its JSON key.
2. Reference it in `eas.json` under `submit.production.android.serviceAccountKeyPath`.
3. Run:
   ```bash
   eas submit --platform android --profile production
   ```

---

## 6. Releasing updates later

1. Make code changes.
2. Bump the **user-facing** version in `app.json` → `expo.version` (e.g. `1.0.1`) when it's a
   meaningful release. (`versionCode` is auto-incremented by EAS — leave it alone.)
3. `eas build --platform android --profile production --non-interactive`
4. Upload the new `.aab` to a release in the Play Console (or `eas submit`).

For pure JS/asset changes you can also explore **EAS Update** (over-the-air updates) later, which
skips the store review for non-native changes.

---

## 7. Quick command cheat sheet

```bash
# Emulator
emulator -avd Pixel_3a_API_32_arm64-v8a            # launch
adb devices                                        # list / check
adb emu kill                                       # stop

# Run app (dev)
cd BlueberryMartApp && npx expo start --android

# Builds
eas build --platform android --profile preview --non-interactive      # APK (test)
eas build --platform android --profile production --non-interactive    # AAB (Play)
eas build:list                                                         # see past builds
eas build:view <BUILD_ID> --json                                       # get artifact URL

# Install an APK on the emulator
adb install -r /path/to/app.apk
```

---

## 8. Troubleshooting

| Symptom | Fix |
|--------|-----|
| `INSTALL_FAILED_INSUFFICIENT_STORAGE` | AVD data partition too small — see §2d (set `disk.dataPartition.size=8192M`). |
| `adb devices` empty | Emulator not running / window closed — relaunch it. |
| App shows network errors / can't log in | The prod backend may be shut down to save billing. See the prod start/stop runbook. |
| `eas` command behaves oddly | You're on an ancient `eas-cli` — `npm install -g eas-cli@latest`. |
| "version code already used" on Play | Shouldn't happen (remote `autoIncrement`); if it does, bump manually or check `appVersionSource`. |
| Build can't find `EXPO_PUBLIC_API_URL` | It's in `eas.json` `env`, not `.env.local` (which is git-ignored). |

---

*Last updated: 2026-06-18. Setup completed: emulator running the app, preview APK verified on
the emulator, production AAB build kicked off. Remaining: create the $25 Play account and upload
the AAB.*
