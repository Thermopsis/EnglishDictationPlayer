#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="${OUTPUT_DIR:-$ROOT/dist}"
APP="$OUT/English Dictation Player.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT/build"
SDK="$(xcrun --sdk macosx --show-sdk-path)"
for ARCH in arm64 x86_64; do
  xcrun swiftc -swift-version 5 -O -sdk "$SDK" -target "${ARCH}-apple-macos13.0" \
    -framework SwiftUI -framework AppKit -framework AVFoundation \
    "$ROOT/Core.swift" "$ROOT/PlayerModel.swift" "$ROOT/Views.swift" "$ROOT/Tests.swift" "$ROOT/UISmoke.swift" \
    -o "$OUT/build/DictationPlayer-$ARCH"
done
lipo -create "$OUT/build/DictationPlayer-arm64" "$OUT/build/DictationPlayer-x86_64" -output "$APP/Contents/MacOS/DictationPlayer"
cp "$ROOT/Info.plist" "$APP/Contents/Info.plist"
mkdir -p "$OUT/build/AppIcon.iconset"
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" "$ROOT/../source/AppIcon.png" --out "$OUT/build/AppIcon.iconset/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" "$ROOT/../source/AppIcon.png" --out "$OUT/build/AppIcon.iconset/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$OUT/build/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
cp "$ROOT/README.md" "$ROOT/../LICENSE.txt" "$APP/Contents/Resources/"
codesign --force --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
lipo -info "$APP/Contents/MacOS/DictationPlayer"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/EnglishDictationPlayer-Mac-Universal.zip"
mkdir -p "$OUT/build/installer"
ditto "$APP" "$OUT/build/installer/English Dictation Player.app"
ln -s /Applications "$OUT/build/installer/Applications" 2>/dev/null || true
hdiutil create -volname "English Dictation Player" -srcfolder "$OUT/build/installer" -ov -format UDZO "$OUT/EnglishDictationPlayer-Mac-Universal.dmg"
(cd "$OUT" && shasum -a 256 EnglishDictationPlayer-Mac-Universal.zip EnglishDictationPlayer-Mac-Universal.dmg > SHA256SUMS.txt)
