#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SVG="${1:-$ROOT_DIR/rfidIcon.svg}"
OUT_DIR="${2:-$ROOT_DIR/dist/icons}"

echo "Generating PNGs and ICO from: $SVG"
dotnet run --project "$ROOT_DIR/tools/IconGen/IconGen.csproj" -- "$SVG" "$OUT_DIR"

# Try to make ICNS if iconutil is available
if command -v iconutil >/dev/null 2>&1; then
  echo "Creating ICNS via iconutil..."
  iconutil -c icns "$OUT_DIR/icon.iconset" -o "$OUT_DIR/app.icns"
  echo "ICNS written: $OUT_DIR/app.icns"
else
  echo "iconutil not found; skip ICNS. Use: iconutil -c icns $OUT_DIR/icon.iconset -o $OUT_DIR/app.icns"
fi

echo "Done. Outputs in $OUT_DIR"

