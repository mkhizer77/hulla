# iOS release build and TestFlight

How to build the iOS app on this Mac and upload it to TestFlight. Like the Play bundle, it is built locally because CI has none of the git-ignored configs (`docs/runbooks/play-release.md`).

## Account
- Apple Developer Program, individual membership. Team "Muhammad Khizer Khalid", team id `B7GXSQMTHV`. The account holder is the Hulla support Apple ID.
- An individual membership gives only the account holder access to certificates and profiles. Other App Store Connect users, even Admins, never see the team in Xcode. So signing and uploads go through an App Store Connect API key instead of an Apple ID in Xcode.
- The key is a Team key with the Admin role. The `.p8` file, its key id and the issuer id live in `~/Documents/hulla-signing/` (`appstoreconnect.txt`), never in the repository. Apple lets you download a key once; a lost key is revoked and replaced under Users and Access, Integrations.
- App id `gg.hulla.app`, registered with `tools/ios-release/asc.py bundle-id gg.hulla.app Hulla`. Its capabilities (23 Nov): In-App Purchase, Associated Domains, Sign in with Apple and Push Notifications; `asc.py capabilities gg.hulla.app` lists them and switches on any named after the id. The App Store Connect app is "Hulla: Desi Games with Friends", SKU `hulla-ios`; the API cannot create apps, so a new app is made by hand under Apps.

## Before a build
The Unity checkout needs the same git-ignored files as the Play build: `HullaServer.local.json`, `HullaSocial.local.json` and `Assets/Firebase/GoogleService-Info.plist`. A release build refuses to start without an https server and its key.

## Build
Raise the build number above the last upload every time. The version can stay until it ships.

```
cd apps/unity/Hulla
HULLA_IOS_TEAM=B7GXSQMTHV HULLA_BUILD_NUMBER=2 HULLA_VERSION=0.1.0 \
/Applications/Unity/Hub/Editor/6000.4.9f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . \
  -buildTarget iOS -executeMethod Hulla.BuildTools.BuildCommands.IOSRelease -logFile -
```

The Xcode project lands in `Builds/iOS/Release`.

## Archive and upload
```
source ~/Documents/hulla-signing/asc.env   # ASC_KEY_ID, ASC_ISSUER_ID, ASC_KEY_PATH
HULLA_IOS_TEAM=B7GXSQMTHV tools/ios-release/ios-release.sh
```

The archive is built unsigned: Xcode's automatic signing asks for a development profile, which a team without registered devices cannot get, and forcing the distribution identity clashes with the Firebase packages. The export step then signs for App Store distribution, making or reusing the distribution certificate and profile through the key, and uploads.

An unsigned archive carries no entitlements, and the export copies them from the archived app's signature. So before the export the script signs the archived app ad hoc with `Unity-iPhone.entitlements` from the Xcode project and stops if one is missing; the export then keeps push, Sign in with Apple and the link domains (checked 23 Nov with a local export: without this step the app comes out with only its app id and team). To check an upload, `HULLA_EXPORT_ONLY=1` with `destination` set to `export` in `ExportOptions.plist` writes the `.ipa` to `Builds/iOS/Archive/export`; unzip it and run `codesign -d --entitlements - Payload/*.app`. If only the upload failed (for example the app record was missing), `HULLA_EXPORT_ONLY=1` uploads the last archive again. The build appears in App Store Connect under TestFlight after processing, about 15 minutes. `tools/ios-release/asc.py builds gg.hulla.app` shows its state.

`ITSAppUsesNonExemptEncryption` is false in Info.plist (`ExportCompliance.cs`): the app uses only the encryption built into iOS, so TestFlight does not ask the export question.

## Testers
- Internal testing: up to 100 App Store Connect users on the team, no review. The internal group "Team" gets every build automatically; the owner's two Apple IDs are in it. Testers install with the TestFlight app from the invite email.
- External testing: up to 10,000 people by email or public link. The first build of each version goes through a short Beta App Review, which needs the test information (contact, description, sign-in notes).

Missing dSYMs for AppLovinSDK and UnityRuntime are reported as warnings on upload; they do not block the build.

## Capabilities
The Xcode project carries three entitlements, each written by its own post-process step into `Unity-iPhone.entitlements`:

| Capability | Switch | Runbook |
|---|---|---|
| Associated Domains (Universal Links) | `InviteLinkXcode.UniversalLinks` | `links-setup.md` |
| Sign in with Apple | `appleSignIn` in `HullaSocial.json` | `sign-in-setup.md` |
| Push Notifications | `PushXcode.Push` | `push-and-purchases-setup.md` |

All three are on. A build signed by a free personal team cannot carry them, so turn the three off for one.

Development builds for a phone sign with the same key: `xcodebuild -allowProvisioningUpdates -allowProvisioningDeviceRegistration -authenticationKeyID … DEVELOPMENT_TEAM=B7GXSQMTHV`. Xcode makes the development profile with the capabilities.

## Still to do on iOS
- Push: an APNs authentication key in Firebase, which only the account holder can make (`push-and-purchases-setup.md`, step 2). Until then iPhones register no token and get no notifications; nothing else breaks.
- Universal Links: the link site must serve the team id `B7GXSQMTHV` in `apple-app-site-association` (`links-setup.md`).
- A round on a phone for each: sign in with Apple, open a shared link, receive and tap each notification type.
