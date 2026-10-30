# Sign in with Apple, Google and Facebook

Players link a provider in Settings → Account so their progress survives a new phone (GDD 10.2, #38). On a new phone they tap the same provider; the app finds the account that already holds it and switches to it, bringing what the phone had (`account_merge`, OQ-015). All three are free.

The app reads the providers' public ids from `apps/unity/Hulla/Assets/_Project/Infrastructure/Social/Resources/HullaSocial.json`. None are secrets. An empty id hides that provider. Desktop builds offer none.

## Google (free)

1. In [Google Cloud](https://console.cloud.google.com) create a project, then APIs & Services → OAuth consent screen: External, app name Hulla, your support email, scopes `openid`, `email`, `profile`.
2. Credentials → Create OAuth client ID, three times:
   - **Web application** ("Hulla server"). Its id goes in `googleWebClientId`: Android asks Google for ID tokens issued to it.
   - **Android**: package `gg.hulla.app` and the SHA-1 of each signing key (the debug key on the build Mac now, the release key later).
   - **iOS**: bundle ID `gg.hulla.app`. Its id goes in `googleIosClientId`; iOS signs in through a browser sheet that returns to the id's reversed scheme.
3. The web client's secret is not used. Do not put it in the repository.

Android uses Credential Manager (`Plugins/Android/HullaGoogleSignIn.java`, libraries in `BuildTools/Editor/HullaSocialDependencies.xml`). iOS uses `ASWebAuthenticationSession` with PKCE (`Plugins/iOS/HullaSocial.mm`, `Infrastructure/Social/GoogleOAuth.cs`). Nakama checks the ID token itself; the server needs no Google settings.

## Facebook (free)

1. In [Meta for Developers](https://developers.facebook.com) create an app with "Authenticate and request data from users with Facebook Login".
2. Add the Android platform (package `gg.hulla.app`, class `com.unity3d.player.UnityPlayerGameActivity`, the key hash of each signing key) and the iOS platform (bundle ID `gg.hulla.app`).
3. Put the App ID in `facebookAppId` and in the server's `social.facebook_limited_login.app_id` (`server/nakama/config/local.yml` and the box's config): iPhones without tracking consent sign in with Limited Login, whose token Nakama checks against it.
   The client token (Settings → Advanced) stays out of the repository. Put it in `apps/unity/Hulla/HullaSocial.local.json` as `{"facebookClientToken": "…"}` (git-ignored), or in `HULLA_FACEBOOK_CLIENT_TOKEN` for CI. A build writes it into Meta's settings asset and the Android manifest and `BuildCommands` clears it afterwards; after a build from the Build window run `git checkout` on those two files. The secret scan fails if it is committed.
4. Builds run `Hulla/Apply sign-in settings` first (`BuildTools/Editor/SocialSetup.cs`), which writes Meta's settings asset and Android manifest from the config.

The app uses Meta's SDK for Unity 18.1.1 with its examples, Windows and IAP extras removed. It asks only for `public_profile`. Friends (`user_friends`) need Meta's app review and come with the Facebook friends work.

## Apple (free with the paid developer account)

Sign in with Apple needs the Apple Developer Program's capability; a personal team cannot sign an app that carries it. When the paid account exists:

1. In the developer portal, enable Sign in with Apple on the `gg.hulla.app` identifier.
2. Set `appleSignIn` to `true`. The iOS build then adds the entitlement (`SocialSetup.OnPostProcessBuild`).
3. The server's `social.apple.bundle_id` is already `gg.hulla.app`.

## Current ids (30 Oct 2026)

- Google Cloud project `hulla-513213` (consent screen "Hulla", External, in testing: only test users can sign in until it is published; the owner's Gmail is a test user). Clients: "Hulla server and web", "Hulla Android debug" (debug key SHA-1), "Hulla iOS".
- Meta app "Hulla", App ID `2149156025694624`, in development mode (only the app's admins and testers can sign in until it is published). Android carries the debug key hash; the Google Play package check is skipped until the app is on Play. iOS carries the bundle ID.
- To publish later: Google needs the app published on the consent screen (basic scopes need no verification); Meta needs a privacy policy URL, a data deletion URL, an icon, a category and business verification.

## People you may know

- Contacts: the phone reads the address book after the player allows it, turns each number into E.164 with the phone's own country for local numbers, and sends them to `friend_suggestions`. The server hashes each number the way phone sign-in does and matches accounts with a verified phone. Numbers are never stored.
- Facebook: the account links Facebook first, then the friend ids come from Facebook (Limited Login's friend list on iOS, `/me/friends` on Android) and match accounts that signed in with Facebook. Only friends who also granted `user_friends` show. Until Meta reviews `user_friends`, only the app's admins, developers and testers get it.
- Testing locally: phone codes on the development server are the last six digits of the number.

## Key hashes

- Android debug key on the build Mac: SHA-1 `5D:42:FC:13:20:98:9C:F0:C3:3D:50:E8:B8:39:0D:82:0C:74:4A:93`, Facebook key hash `XUL8EyCYnPDDPVDouDkNggx0SpM=`.
- Release key: add its SHA-1 to the Google Android client and its key hash to Meta when the release keystore exists (`docs/runbooks/github-and-ci-setup.md`).

## Building

- Android: Meta's and Google's libraries are listed in `*Dependencies.xml`; run `-executeMethod Hulla.BuildTools.AndroidDependencies.Resolve` after changing them, which writes them into `Plugins/Android/mainTemplate.gradle`.
- iOS: the export carries a Podfile for Meta's libraries. Run `pod install --repo-update` in `Builds/iOS/Xcode` (under Rosetta, `arch -x86_64`, if your CocoaPods gems are Intel builds), then build `Unity-iPhone.xcworkspace` rather than the `.xcodeproj`.
