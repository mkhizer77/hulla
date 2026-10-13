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

Arrives with #58.
