# Firebase on the client

The app uses Firebase for Analytics, Crashlytics and the Android push token (#71, #72). Remote config stays on our own server (`config_get`), so Firebase Remote Config is not used.

## The project

- Firebase project **Hulla** on the owner's personal Google account, added to the existing Google Cloud project `hulla-513213` (the one holding the Google sign-in clients).
- Apps: Android `gg.hulla.app` and iOS `gg.hulla.app`, registered as one Unity app.
- Packages: Firebase Unity SDK 14.1.0 (App, Analytics, Crashlytics, Messaging) and the External Dependency Manager 1.2.190, vendored as `.tgz` files in `apps/unity/Hulla/Packages/firebase` (Git LFS). Google no longer runs a package registry for them; to update, download the new files from `https://dl.google.com/games/registry/unity/<package>/<package>-<version>.tgz` and change `Packages/manifest.json`.

## Config files (kept out of the repository)

`google-services.json` and `GoogleService-Info.plist` hold the project's API key, so they stay out of the public repository like the Facebook client token.

1. Firebase console → Project settings → Your apps → download both files.
2. Put them in `apps/unity/Hulla/Assets/Firebase/` in every checkout that builds the app. Git ignores them there, and ignores the files Firebase generates from them (`Assets/Plugins/Android/FirebaseApp.androidlib/`, `Assets/StreamingAssets/google-services-desktop.json`).
3. A build without them still runs; Firebase reports that it is unavailable and the app carries on without Analytics, Crashlytics or push.
4. In Google Cloud → APIs & Services → Credentials, restrict the Android and iOS API keys to the app's package name and signing fingerprints, and the bundle id.

Release builds on CI will need both files as repository secrets written into place before the build; add that with the release job.

## Consent

Nothing is collected before the player says yes to "Share anonymous usage data" (GDD 23.6). `FirebaseConsentDefaults` turns Analytics, Crashlytics and the advertising id off in the Android manifest and the iOS Info.plist, and denies analytics and ad storage by default. `FirebaseServices.SetConsent` turns Analytics and Crashlytics on after the yes, and off again when the setting is switched off. Ad storage, ad user data and ad personalisation stay denied.

## Push

On Android the FCM token goes to the server's `push_register` after sign-in, and again when it changes. The server side is in `push-and-purchases-setup.md`. iOS waits for the paid Apple account: it needs an APNs key uploaded to Firebase, and asking for the token there would show the notification dialog at launch.

## Checks

- Analytics: on a development build, run `adb shell setprop debug.firebase.analytics.app gg.hulla.app`, say yes to usage data, play a round, and watch Firebase console → Analytics → DebugView. Say no on a fresh install: DebugView stays empty.
- Crashlytics: with consent on, force a crash in a development build; it shows in Firebase console → Crashlytics after the next launch.
- Push: after sign-in, the server's `admin_push_test` lists the device (step 5 of `push-and-purchases-setup.md`).
