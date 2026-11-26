#!/bin/bash
# 일해라.app 을 만든다 (macOS, Xcode 명령행 도구 필요).
#   ./build-app.sh            → build/일해라.app, build/Ilhaera-mac.zip
#   VERSION=1.5.0 ./build-app.sh
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${VERSION:-1.5.0}"
BUILD="${BUILD:-1}"
APP="build/일해라.app"

# Apple 실리콘 + 인텔 모두 도는 유니버설 바이너리
swift build -c release --arch arm64 --arch x86_64
BIN_DIR="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Ilhaera" "$APP/Contents/MacOS/Ilhaera"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD/" Resources/Info.plist > "$APP/Contents/Info.plist"
iconutil -c icns Resources/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"

# 애플 개발자 서명이 없으므로 임시(ad-hoc) 서명
codesign --force --deep --sign - "$APP"
codesign --verify --verbose "$APP"

(cd build && ditto -c -k --sequesterRsrc --keepParent "일해라.app" "Ilhaera-mac.zip")
lipo -info "$APP/Contents/MacOS/Ilhaera"
ls -la build
