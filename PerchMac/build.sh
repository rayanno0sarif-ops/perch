#!/bin/sh
# Builds Perch.app on a Mac with Xcode or the Command Line Tools.
set -e
swift build -c release
APP=Perch.app
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS"
cp .build/release/Perch "$APP/Contents/MacOS/Perch"
cp Info.plist "$APP/Contents/Info.plist"
codesign --force --sign - "$APP" 2>/dev/null || true
echo "Built $APP. Open it, then grant Accessibility, Screen Recording and Input Monitoring when asked."
