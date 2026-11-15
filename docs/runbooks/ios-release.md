# iOS release build and TestFlight

How to build the iOS app on this Mac and upload it to TestFlight. Like the Play bundle, it is built locally because CI has none of the git-ignored configs (`docs/runbooks/play-release.md`).

## Account
- Apple Developer Program, individual membership. Team "Muhammad Khizer Khalid", team id `B7GXSQMTHV`. The account holder is the Hulla support Apple ID.
- An individual membership gives only the account holder access to certificates and profiles. Other App Store Connect users, even Admins, never see the team in Xcode. So signing and uploads go through an App Store Connect API key instead of an Apple ID in Xcode.
- The key is a Team key with the Admin role. The `.p8` file, its key id and the issuer id live in `~/Documents/hulla-signing/` (`appstoreconnect.txt`), never in the repository. Apple lets you download a key once; a lost key is revoked and replaced under Users and Access, Integrations.
- App id `gg.hulla.app`, registered with `tools/ios-release/asc.py bundle-id gg.hulla.app Hulla`. The App Store Connect app is "Hulla: Desi Games with Friends", SKU `hulla-ios`; the API cannot create apps, so a new app is made by hand under Apps.

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

The archive is built unsigned: Xcode's automatic signing asks for a development profile, which a team without registered devices cannot get, and forcing the distribution identity clashes with the Firebase packages. The export step then signs for App Store distribution, making or reusing the distribution certificate and profile through the key, and uploads. If only the upload failed (for example the app record was missing), `HULLA_EXPORT_ONLY=1` uploads the last archive again. The build appears in App Store Connect under TestFlight after processing, about 15 minutes. `tools/ios-release/asc.py builds gg.hulla.app` shows its state.

`ITSAppUsesNonExemptEncryption` is false in Info.plist (`ExportCompliance.cs`): the app uses only the encryption built into iOS, so TestFlight does not ask the export question.

## Testers
- Internal testing: up to 100 App Store Connect users on the team, no review. The internal group "Team" gets every build automatically; the owner's two Apple IDs are in it. Testers install with the TestFlight app from the invite email.
- External testing: up to 10,000 people by email or public link. The first build of each version goes through a short Beta App Review, which needs the test information (contact, description, sign-in notes).

Missing dSYMs for AppLovinSDK and UnityRuntime are reported as warnings on upload; they do not block the build.

## Not yet on iOS
Push (the APNs key and the `aps-environment` entitlement), Sign in with Apple and Universal Links wait for their own changes; see `docs/runbooks/sign-in-setup.md` and `docs/runbooks/links-setup.md`.
