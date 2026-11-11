# Play release build

How to make the signed Android App Bundle for Google Play from this Mac. CI's `release-android` job builds from a tag, but it has none of the git-ignored configs (Firebase, Facebook, the server key), so uploads are built locally until those move into secrets.

## Upload key
- The upload keystore is `~/Documents/hulla-signing/hulla-upload.keystore` (PKCS12, alias `hulla-upload`, RSA 4096, valid to 2054). It is never in the repository.
- Its password is in the macOS Keychain under "Hulla Android upload keystore". The store and key passwords are the same.
- The same keystore and password are in the repository secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_NAME` and `ANDROID_KEYALIAS_PASS`.
- Keep a second copy of the keystore and its password off this Mac. Play App Signing holds the app signing key, so a lost upload key can be reset from Play Console, but that takes days.
- Upload key SHA-256: `74:CF:DF:1D:52:A5:A0:3D:C6:E8:71:3D:B8:79:BA:5E:2D:1B:1C:A4:ED:C8:26:64:39:81:FB:C5:42:F0:86:F6`.

## Before a build
The checkout needs the git-ignored files: `HullaServer.local.json` (the phase 0 box and its server key), `HullaSocial.local.json`, and `Assets/Firebase/google-services.json`. A release build refuses to start without an https server and its key.

## Build
Raise the version code above the last upload every time.

```
cd apps/unity/Hulla
HULLA_KEYSTORE_PASS="$(security find-generic-password -a hulla-upload -s 'Hulla Android upload keystore' -w)" \
HULLA_VERSION_CODE=2 HULLA_VERSION=0.1.1 \
/Applications/Unity/Hub/Editor/6000.4.9f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . \
  -buildTarget Android -executeMethod Hulla.BuildTools.BuildCommands.AndroidRelease -logFile -
```

The bundle lands in `Builds/Android/hulla-<version>-<code>.aab` with a `.symbols.zip` beside it. Upload both in Play Console (the symbols under the release's App bundle explorer, Downloads, Native debug symbols).

A release build always talks to the server it was built for; the command line and the Settings server field only work in development builds.

After the build, Unity writes `AndroidKeystoreName: '{inproject}: '` into `ProjectSettings.asset`. Discard it with `git checkout -- ProjectSettings/ProjectSettings.asset`.

## Check the signature
```
keytool -printcert -jarfile Builds/Android/hulla-0.1.0-1.aab
```
The SHA-256 must match the upload key above.
