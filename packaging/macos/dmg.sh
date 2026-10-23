#!/usr/bin/env bash
# macOS .app 打包 + DMG：packaging/macos/dmg.sh <publishDir>
#   产出 dist/Erya_<版本>_<目录名>.dmg，内含 Erya.app（拖入 Applications 即用）
# publishDir = publish.ps1 的自包含输出目录（如 artifacts/publish/osx_arm64），
# 目录名进产物文件名。仅 macOS 主机可跑（hdiutil）。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PUB="${1:?用法：packaging/macos/dmg.sh <publishDir>}"
PUB="$(cd "$PUB" && pwd)"

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT/src/Erya/Erya.csproj" | head -1)"
if [[ -z "$VERSION" ]]; then
  echo "错误：无法从 src/Erya/Erya.csproj 读取 <Version>。" >&2
  exit 1
fi

STAGE_NAME="$(basename "$PUB")"
OUT="$ROOT/dist/Erya_${VERSION}_${STAGE_NAME}.dmg"

# .app 在临时目录组装，避免把 publish 目录拷进自身
STAGE_ROOT="$(mktemp -d)"
APP="$STAGE_ROOT/Erya.app"
mkdir -p "$APP/Contents/MacOS"
cp -a "$PUB/." "$APP/Contents/MacOS/"
find "$APP" -name '*.pdb' -delete

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Erya</string>
  <key>CFBundleDisplayName</key><string>尔雅 Erya</string>
  <key>CFBundleIdentifier</key><string>irihi.erya</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>Erya</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# 首次运行 Gatekeeper 拦未签名应用：右键打开 / xattr -cr，README 已注明
mkdir -p "$ROOT/dist"
rm -f "$OUT"
hdiutil create -volname "Erya $VERSION" -srcfolder "$STAGE_ROOT" -ov -format UDZO "$OUT" >/dev/null
rm -rf "$STAGE_ROOT"

# 注意：macOS 系统 bash 是 3.2，$VAR 后紧跟全角字符会被并进变量名（set -u 即炸）——一律 ${VAR} 定界
echo "==> ${OUT} ($(du -h "$OUT" | cut -f1))"
