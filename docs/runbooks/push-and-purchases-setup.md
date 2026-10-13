# Push notifications and purchases: setting up the box

The server sends push through Firebase Cloud Messaging and validates store receipts through Nakama. Both wait for credentials only the owner can create. Nothing here goes into the repository: every value lands in the box's environment or Nakama config (runbook "Hardening the box" in `deploy-and-rollback.md`).

## Push (GDD 10.10)

1. Firebase project: create one for Hulla (the same project the Unity client uses for Analytics and Crashlytics). Add the Android app with the package name and the iOS app with the bundle id.
2. iOS delivery: in the Apple Developer account create an APNs authentication key (.p8) and upload it to the Firebase project under Cloud Messaging. One FCM path then serves both platforms.
3. Service account: in Google Cloud for that project, create a service account with the role "Firebase Cloud Messaging API Admin" and download its JSON key.
4. Box environment (`runtime.env` in the Nakama config): `HULLA_FCM_PROJECT_ID` (the `project_id` of the key), `HULLA_FCM_CLIENT_EMAIL` (`client_email`), `HULLA_FCM_PRIVATE_KEY` (`private_key`, kept on one line with the newlines written as `\n`). Restart Nakama.
5. Check from a device: sign in on the device, let the client call `push_register`, then run
   `curl -s -X POST "http://127.0.0.1:7350/v2/rpc/admin_push_test?http_key=<key>&unwrap" -d '{"userId":"<user id>","type":"table_invite"}'`
   The answer lists each device and its outcome: `sent`, `invalid_token` (the token was dropped), `failed` (quota or network; the log line `push request failed` has the cause) or `unconfigured` (a variable is missing). Repeat for `friend_request`, `friend_online`, `daily_reward` and `season_end`; each must open its screen (lobby, friends, friends, home, pass).
6. Schedule `admin_push_sweep` hourly from the cron sidecar (#76) next to `admin_purge_deleted`.

The server never writes notification text. The push carries `push.<type>.title` and `push.<type>.body` as localisation keys with their arguments (a display name, a room code, hours left), so the client's string table must hold those ten keys in every language.

## Purchases (GDD 13.4, 20.9)

1. Products: create the nine products of `content/products/v1.json` in App Store Connect and Google Play Console with the same ids (`remove_ads`, `gems_120`, `gems_650`, `gems_1400`, `gems_3000`, `gems_8000`, `pass_premium`, `pass_premium_plus`, `starter_bundle`). Remove Ads and the starter bundle are non-consumable, the rest consumable. Set local prices from the USD tier.
2. Apple: in App Store Connect, generate the app-specific shared secret and put it in the Nakama config as `iap.apple.shared_password`. Add sandbox testers under Users and Access.
3. Google: in Google Play Console, link a Google Cloud project and create a service account with access to the Play Developer API (role "Finance" is not needed; grant "View financial data" off and "Manage orders and subscriptions" on). Put its `client_email` and `private_key` in the Nakama config under `iap.google`. Add licence testers under Setup, so sandbox purchases cost nothing.
4. Restart Nakama. `iap_verify` now answers `ok` or `economy.receipt_invalid`; before this step it answers `economy.store_unavailable`.
5. Check on each store: make a sandbox purchase of `gems_120` from the device; `wallet_get` shows 120 gems once, and sending the same receipt again applies nothing. Buy `remove_ads`, reinstall, sign in and call `iap_restore`: Remove Ads is back. Send a doctored receipt: `economy.receipt_invalid` and a `receipt refused` line in the log.
6. Refunds: Nakama records Apple refunds through its App Store notifications endpoint and Google refunds through the Voided Purchases check when those are configured; the server does not take gems back on its own (OQ-033). A refund that matters goes through `admin_grant` with a negative-free correction: grant nothing, set a note in the audit, and ban the account if it is abuse (moderation runbook).
