#!/usr/bin/env bash
# Archive the Xcode project made by BuildCommands.IOSRelease and upload it to App Store Connect.
# Signing and the upload use an App Store Connect API key, so no Apple ID has to be signed in to
# Xcode (docs/runbooks/ios-release.md).
# Usage: HULLA_IOS_TEAM=… ASC_KEY_ID=… ASC_ISSUER_ID=… ASC_KEY_PATH=…/AuthKey_….p8 \
#   tools/ios-release/ios-release.sh [path/to/Builds/iOS/Release]
set -euo pipefail

team="${HULLA_IOS_TEAM:?set HULLA_IOS_TEAM to the Apple Developer team id}"
auth=(-allowProvisioningUpdates
  -authenticationKeyID "${ASC_KEY_ID:?set ASC_KEY_ID}"
  -authenticationKeyIssuerID "${ASC_ISSUER_ID:?set ASC_ISSUER_ID}"
  -authenticationKeyPath "${ASC_KEY_PATH:?set ASC_KEY_PATH to the .p8 file}")
here="$(cd "$(dirname "$0")" && pwd)"
project_dir="${1:-$here/../../apps/unity/Hulla/Builds/iOS/Release}"
project_dir="$(cd "$project_dir" && pwd)"
out="$project_dir/../Archive"
archive="$out/Hulla.xcarchive"

if [ -d "$project_dir/Unity-iPhone.xcworkspace" ]; then
  source=(-workspace "$project_dir/Unity-iPhone.xcworkspace")
else
  source=(-project "$project_dir/Unity-iPhone.xcodeproj")
fi

# HULLA_EXPORT_ONLY=1 uploads the last archive again, for example after the app record was missing.
if [ "${HULLA_EXPORT_ONLY:-0}" != 1 ]; then
  rm -rf "$out"
  mkdir -p "$out"
  xcodebuild archive "${source[@]}" -scheme Unity-iPhone -configuration Release \
    -destination generic/platform=iOS -archivePath "$archive" \
    DEVELOPMENT_TEAM="$team" CODE_SIGNING_ALLOWED=NO
fi
rm -rf "$out/export"

# The archive is unsigned: automatic signing would want a development profile, and a new team has
# no devices for one. The export signs for App Store distribution and makes the certificate and
# profile through the key.
#
# An unsigned app carries no entitlements, and the export takes them from the archived app's
# signature, so push, Sign in with Apple and Universal Links would be dropped without a word.
# Sign the archive ad hoc with the project's entitlements first; the export replaces the signature
# and keeps them.
entitlements="$project_dir/Unity-iPhone.entitlements"
if [ -f "$entitlements" ]; then
  app="$(find "$archive/Products/Applications" -maxdepth 1 -name '*.app' | head -1)"
  [ -n "$app" ] || { echo "no app in $archive" >&2; exit 1; }
  if [ -d "$app/Frameworks" ]; then
    find "$app/Frameworks" -depth \( -name '*.framework' -o -name '*.dylib' \) \
      -exec codesign --force --sign - {} \;
  fi
  codesign --force --sign - --entitlements "$entitlements" "$app"
  signed="$(codesign -d --entitlements - --xml "$app" 2>/dev/null)"
  for key in $(/usr/libexec/PlistBuddy -c Print "$entitlements" | sed -n 's/^    \([^ ]*\) = .*/\1/p'); do
    case "$signed" in
      *"<key>$key</key>"*) ;;
      *) echo "entitlement $key is missing from the archived app" >&2; exit 1 ;;
    esac
  done
fi

sed "s/__TEAM__/$team/" "$here/ExportOptions.plist" > "$out/ExportOptions.plist"
xcodebuild -exportArchive -archivePath "$archive" -exportOptionsPlist "$out/ExportOptions.plist" \
  -exportPath "$out/export" "${auth[@]}"

echo "Uploaded. The build shows in App Store Connect, TestFlight, after processing (about 15 minutes)."
