#!/usr/bin/env bash
# macOS .app 打包 + DMG：packaging/macos/dmg.sh <publishDir>
#   产出 dist/Erya_<版本>_<目录名>.dmg，内含 Erya.app（拖入 Applications 即用）
# publishDir = publish.ps1 的自包含输出目录（如 artifacts/publish/osx_arm64），
# 目录名进产物文件名。仅 macOS 主机可跑（hdiutil）；需要 node + npm（写 .DS_Store）。
#
# 拖拽安装布局（.DS_Store 纯生成，不经 Finder）：
#   macOS 15+ 上 Finder AppleScript 的 set position 静默不落盘（Iloc 不写）、
#   实机设置的 icvp 背景还有认卷时序竞态——都已踩坑弃用。改用 npm 包 ds-store
#   直接生成完整 .DS_Store（窗口/背景/图标大小/图标位置），确定性无竞态。
# 背景图资产（同目录）：dmg-background.png / dmg-background@2x.png
#   （Swift CoreGraphics 画：渐变底 + 靛青箭头 + 「拖动 Erya 到 Applications 文件夹」）
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
VOLNAME="Erya $VERSION"

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

# 拖拽安装布局的卷内容：Applications 别名 + 背景图 + 卷图标（点开头=默认隐藏）
ln -s /Applications "$STAGE_ROOT/Applications"
mkdir -p "$STAGE_ROOT/.background"
cp "$MACOS_DIR/dmg-background.png" "$MACOS_DIR/dmg-background@2x.png" "$STAGE_ROOT/.background/"
cp "$MACOS_DIR/Erya.icns" "$STAGE_ROOT/.VolumeIcon.icns"

# 先打可写镜像并挂载：ds-store 的背景 alias 必须在真实卷上生成（卷名/路径才与挂载后一致）。
# 不固定 mountpoint（同名卷残留挂载会冲突），从 attach 输出解析实际挂载路径。
RW="$(mktemp -t erya-rw).dmg"
hdiutil create -volname "$VOLNAME" -srcfolder "$STAGE_ROOT" -ov -format UDRW "$RW" >/dev/null 2>&1
rm -rf "$STAGE_ROOT"
MOUNT="$(hdiutil attach -nobrowse "$RW" | tail -1 | awk -F'\t' '{print $3}')"
if [[ -z "$MOUNT" || ! -d "$MOUNT" ]]; then
  echo "错误：RW 镜像挂载失败（$RW）。" >&2
  exit 1
fi

# 生成 .DS_Store（node + ds-store；npm 装在临时目录，不动仓库；cwd=卷根，背景用卷内绝对路径）
DEPS="$(mktemp -d)"
npm install --prefix "$DEPS" --no-audit --no-fund --loglevel=error ds-store >/dev/null
DS="$MOUNT/.DS_Store"
( cd "$MOUNT" && DS_OUT="$DS" MOUNT="$MOUNT" NODE_PATH="$DEPS/node_modules" node <<'JS'
const DSStore = require('ds-store');
const f = new DSStore();
// 图标摆位（Iloc）由 ds-store 直接写入（文件名在 DS_Store 内为 UTF-16BE）。
// Erya 左 / Applications 右，y=185 避开顶部横幅；Finder AppleScript 的 set position
// 在 macOS 15+ 静默失效不可用。背景为顶部横幅文案（与摆位无坐标耦合）。
f.setBackgroundPath(process.env.DS_OUT.replace(/\.DS_Store$/, '.background/dmg-background.png'));
f.setIconSize(128);
f.setIconPos('Erya.app', 120, 185);
f.setIconPos('Applications', 410, 185);
f.setWindowPos(180, 120);
f.setWindowSize(660, 400);
f.vSrn(1);
f.write(process.env.DS_OUT, (err) => { if (err) throw err; console.log('DS_Store written'); });
JS
)
rm -rf "$DEPS"

# 自检：背景引用 + 两个图标的 Iloc（注意 DS_Store 文件名是 UTF-16BE，ASCII strings 搜不到——曾因此误判）
python3 - "$DS" <<'PY'
import sys
data = open(sys.argv[1], 'rb').read()
missing = [n for n in (b'dmg-background', 'Erya.app'.encode('utf-16-be'), 'Applications'.encode('utf-16-be')) if n not in data]
if missing:
    print('错误：.DS_Store 自检失败：', missing, file=sys.stderr)
    sys.exit(1)
print('DS_Store 自检通过')
PY

# 卸载并转只读 UDZO（压缩分发格式）；detach 带重试——元数据服务偶尔短暂占用
for i in 1 2 3; do
  if hdiutil detach "$MOUNT" >/dev/null 2>&1; then break; fi
  [[ $i -eq 3 ]] && { hdiutil detach -force "$MOUNT" >/dev/null 2>&1 || true; sleep 3; }
  sleep 2
done
mkdir -p "$ROOT/dist"
rm -f "$OUT"
hdiutil convert "$RW" -format UDZO -o "$OUT" -ov >/dev/null
rm -f "$RW"

echo "==> ${OUT}（$(du -h "$OUT" | cut -f1)）"
