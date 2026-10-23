#!/usr/bin/env bash
# macOS .app 打包 + DMG：packaging/macos/dmg.sh <publishDir>
#   产出 dist/Erya_<版本>_<目录名>.dmg，内含 Erya.app（拖入 Applications 即用）
# publishDir = publish.ps1 的自包含输出目录（如 artifacts/publish/osx_arm64），
# 目录名进产物文件名。仅 macOS 主机可跑（hdiutil）。
#
# 拖拽安装布局的三个预生成资产（同目录）：
#   Erya.icns            应用图标（packaging/linux/erya.svg → 1024px → iconutil）
#   dmg-background[@2x].png  背景提示图（箭头 + 「拖动 Erya 到 Applications 文件夹」）
#   dmg.DS_Store         Finder 视图模板（660×400 窗口、背景图、128pt 图标、
#                        Erya.app(80,151) / Applications(480,151) 摆位）
#   模板由 Finder 自动化在临时 UDRW 卷上设置后拷出；调摆位请如法重生成，勿手改二进制。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MACOS_DIR="$ROOT/packaging/macos"
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
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -a "$PUB/." "$APP/Contents/MacOS/"
find "$APP" -name '*.pdb' -delete
cp "$MACOS_DIR/Erya.icns" "$APP/Contents/Resources/"

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
  <key>CFBundleIconFile</key><string>Erya</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# 拖拽安装布局：Applications 别名 + 背景图 + Finder 视图模板
ln -s /Applications "$STAGE_ROOT/Applications"
mkdir -p "$STAGE_ROOT/.background"
cp "$MACOS_DIR/dmg-background.png" "$MACOS_DIR/dmg-background@2x.png" "$STAGE_ROOT/.background/"
cp "$MACOS_DIR/dmg.DS_Store" "$STAGE_ROOT/.DS_Store"

# 首次运行 Gatekeeper 拦未签名应用：右键打开 / xattr -cr，README 已注明
mkdir -p "$ROOT/dist"
rm -f "$OUT"
hdiutil create -volname "Erya $VERSION" -srcfolder "$STAGE_ROOT" -ov -format UDZO "$OUT" >/dev/null
rm -rf "$STAGE_ROOT"

echo "==> ${OUT}（$(du -h "$OUT" | cut -f1)）"
