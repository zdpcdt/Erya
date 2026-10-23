#!/usr/bin/env bash
# 构建 Linux 发行三件套：packaging/linux/dist.sh [RID]（默认 linux-x64）
#   dist/erya-<版本>-<rid>.tar.gz      自包含 + install.sh（用户级安装，无需 root）
#   dist/Erya_<版本>_<arch>.AppImage   便携格式（appimagetool，首次构建自动下载）
#   dist/erya_<版本>-1_<debarch>.deb   Debian/Ubuntu 安装包（dpkg-deb）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

RID="${1:-linux-x64}"
case "$RID" in
  linux-x64)   APPARCH=x86_64;  DEBARCH=amd64 ;;
  linux-arm64) APPARCH=aarch64; DEBARCH=arm64 ;;
  *) echo "不支持的 RID：${RID}（支持 linux-x64 / linux-arm64）" >&2; exit 1 ;;
esac

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/Erya/Erya.csproj | head -1)"
if [[ -z "$VERSION" ]]; then
  echo "错误：无法从 src/Erya/Erya.csproj 读取 <Version>。" >&2
  exit 1
fi

# ---------- 1. publish 一次，三种格式共用 ----------

PUB="dist/publish-$RID"
echo "==> dotnet publish（self-contained，${RID}）"
rm -rf "$PUB"
dotnet publish src/Erya -c Release -r "$RID" --self-contained true -o "$PUB"

desktop_exec() { # $1 = Exec 值；输出到 stdout
  sed -e "s|^Exec=.*|Exec=$1|" -e "s|^TryExec=.*|TryExec=$1|" packaging/linux/erya.desktop.in
}

# ---------- 2. tar.gz（用户级安装） ----------

echo "==> tar.gz"
TAR_DIR="dist/erya-$VERSION-$RID"
rm -rf "$TAR_DIR"
mkdir -p "$TAR_DIR"
cp -a "$PUB/." "$TAR_DIR/"
install -m755 -t "$TAR_DIR" packaging/linux/install.sh packaging/linux/uninstall.sh
install -m644 -t "$TAR_DIR" packaging/linux/erya.desktop.in packaging/linux/erya.svg
tar -C dist -czf "dist/erya-$VERSION-$RID.tar.gz" "erya-$VERSION-$RID"
rm -rf "$TAR_DIR"

# ---------- 3. AppImage ----------

echo "==> AppImage（${APPARCH}）"
APPDIR="dist/Erya.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr"
cp -a "$PUB/." "$APPDIR/usr/"

install -Dm644 packaging/linux/erya.svg "$APPDIR/erya.svg"
install -Dm644 packaging/linux/erya.svg "$APPDIR/usr/share/icons/hicolor/scalable/apps/erya.svg"
# AppImage 桌面入口按 AppImage 规范只保留 Exec（AppRun 会被运行时调用）
sed -e "s|^Exec=.*|Exec=Erya|" -e "/^TryExec=/d" packaging/linux/erya.desktop.in > "$APPDIR/erya.desktop"
install -Dm644 "$APPDIR/erya.desktop" "$APPDIR/usr/share/applications/erya.desktop"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/usr/bin/env bash
# 尔雅 Erya AppImage 入口：直接执行自包含主程序。
set -e
HERE="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
exec "$HERE/usr/Erya" "$@"
APPRUN
chmod 755 "$APPDIR/AppRun"

TOOL="dist/cache/appimagetool-$APPARCH.AppImage"
if [[ ! -x "$TOOL" ]]; then
  mkdir -p dist/cache
  echo "    下载 appimagetool（${APPARCH}）…"
  wget -q -O "$TOOL" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$APPARCH.AppImage"
  chmod +x "$TOOL"
fi
# --appimage-extract-and-run：不依赖宿主机 FUSE。
# 注意：aarch64 目标需在 arm64 主机（或 qemu-user）上运行本脚本。
"$TOOL" --appimage-extract-and-run "$APPDIR" "dist/Erya_${VERSION}_${APPARCH}.AppImage" >/dev/null
rm -rf "$APPDIR"

# ---------- 4. deb ----------

echo "==> deb（${DEBARCH}）"
DEBROOT="dist/debroot"
rm -rf "$DEBROOT"
mkdir -p "$DEBROOT/usr/lib/erya" "$DEBROOT/usr/bin" \
         "$DEBROOT/usr/share/applications" \
         "$DEBROOT/usr/share/icons/hicolor/scalable/apps" \
         "$DEBROOT/usr/share/doc/erya" "$DEBROOT/DEBIAN"
cp -a "$PUB/." "$DEBROOT/usr/lib/erya/"

cat > "$DEBROOT/usr/bin/erya" <<'WRAPPER'
#!/usr/bin/env bash
exec /usr/lib/erya/Erya "$@"
WRAPPER
chmod 755 "$DEBROOT/usr/bin/erya"

desktop_exec erya > "$DEBROOT/usr/share/applications/erya.desktop"
install -Dm644 packaging/linux/erya.svg "$DEBROOT/usr/share/icons/hicolor/scalable/apps/erya.svg"

MAINTAINER_NAME="$(git config user.name || echo 'Erya')"
MAINTAINER_EMAIL="$(git config user.email || echo 'erya@localhost')"
SIZE_KB="$(du -sk "$DEBROOT/usr" | cut -f1)"

cat > "$DEBROOT/DEBIAN/control" <<CONTROL
Package: erya
Version: $VERSION-1
Section: utils
Priority: optional
Architecture: $DEBARCH
Maintainer: $MAINTAINER_NAME <$MAINTAINER_EMAIL>
Installed-Size: $SIZE_KB
Depends:
Description: 中州韵输入法引擎（RIME）的可视化配置工具
 尔雅 Erya —— 不写 YAML，也能把 RIME 调教得服服帖帖。支持小狼毫、
 fcitx5-rime、ibus-rime、鼠须管的方案管理、输入/外观设置、高级配置
 与备份恢复。
CONTROL

cat > "$DEBROOT/usr/share/doc/erya/copyright" <<COPYRIGHT
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: erya
Source: local build

Files: *
Copyright: 2026 $MAINTAINER_NAME
License: MIT
 以 MIT 许可证发布；详见随源码附带的 LICENSE 文件。
COPYRIGHT

dpkg-deb --build --root-owner-group "$DEBROOT" "dist/erya_${VERSION}-1_${DEBARCH}.deb" >/dev/null
rm -rf "$DEBROOT" "$PUB"

# ---------- 汇总 ----------

echo
echo "完成，产出（用户安装方式各异）："
ls -lh "dist/erya-$VERSION-$RID.tar.gz" \
       "dist/Erya_${VERSION}_${APPARCH}.AppImage" \
       "dist/erya_${VERSION}-1_${DEBARCH}.deb" | awk '{print "  " $NF "  (" $5 ")"}'
echo "  tar.gz：解压后 ./install.sh    AppImage：chmod +x 双击运行    deb：sudo apt install ./erya_*.deb"
