#!/usr/bin/env bash
# Wraps an osx-arm64 publish folder into "Gravicode CAD Studio.app", a zipped .app and a .dmg.
# Signed with MACOS_SIGN_IDENTITY when set (the workflow then notarises the .dmg); ad-hoc signed otherwise.
#   package-macos.sh <publish-dir> <version> <out-dir>
set -euo pipefail
SRC=$1; VER=$2; OUT=$3
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$OUT"
STAGE=$(mktemp -d)
APP="$STAGE/Gravicode CAD Studio.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -r "$SRC"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/GravicodeCadStudio"

# icon: 512 px PNG -> iconset -> icns
ICONSET=$(mktemp -d)/icon.iconset
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$HERE/../../assets/icon.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  if [ $d -le 512 ]; then sips -z $d $d "$HERE/../../assets/icon.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null; fi
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/GravicodeCadStudio.icns"

sed "s/__VERSION__/$VER/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"

# Developer ID signing with the hardened runtime when MACOS_SIGN_IDENTITY is set (release workflow);
# otherwise an ad-hoc signature so Apple silicon will still launch the app.
if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
  find "$APP/Contents/MacOS" -type f \( -name "*.dylib" -o -perm -u+x \) -print0 | while IFS= read -r -d '' f; do
    codesign --force --timestamp --options runtime --entitlements "$HERE/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$f"
  done
  codesign --force --timestamp --options runtime --entitlements "$HERE/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$APP"
else
  codesign --force --deep --sign - "$APP" || true
fi
ditto -c -k --keepParent "$APP" "$OUT/GravicodeCadStudio-$VER-osx-arm64.app.zip"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Gravicode CAD Studio" -srcfolder "$STAGE" -ov -format UDZO "$OUT/GravicodeCadStudio-$VER-osx-arm64.dmg"
ls -la "$OUT"
