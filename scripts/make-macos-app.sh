#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_NAME="Pruebas RFID Prime"
PUBLISH_DIR="$ROOT_DIR/dist/macos-publish"
APP_DIR="$ROOT_DIR/dist/$APP_NAME.app"
ICNS_PATH="$ROOT_DIR/dist/icons/app.icns"

echo "Publishing app (self-contained) for macOS..."
dotnet publish "$ROOT_DIR/src/UhfPrime.TestBench/UhfPrime.TestBench.csproj" \
  -c Release -r osx-arm64 -p:UseAppHost=true -p:SelfContained=true -o "$PUBLISH_DIR"

echo "Creating .app bundle at: $APP_DIR"
BIN_NAME="UhfPrime.TestBench"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

# Copy executable
cp "$PUBLISH_DIR/$BIN_NAME" "$APP_DIR/Contents/MacOS/$APP_NAME"
chmod +x "$APP_DIR/Contents/MacOS/$APP_NAME"

# Copy ICNS
if [ -f "$ICNS_PATH" ]; then
  cp "$ICNS_PATH" "$APP_DIR/Contents/Resources/app.icns"
else
  echo "Warning: ICNS not found at $ICNS_PATH. Run scripts/make-icons.sh first."
fi

cat > "$APP_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Pruebas RFID Prime</string>
  <key>CFBundleDisplayName</key>
  <string>Pruebas RFID Prime</string>
  <key>CFBundleIdentifier</key>
  <string>com.example.pruebasrfidprime</string>
  <key>CFBundleVersion</key>
  <string>1.0.0</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleExecutable</key>
  <string>Pruebas RFID Prime</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
  <key>CFBundleIconFile</key>
  <string>app</string>
</dict>
</plist>
PLIST

echo "macOS .app created: $APP_DIR"
echo "Optional: codesign --force --deep --sign - \"$APP_DIR\""

