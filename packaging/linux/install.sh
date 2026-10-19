#!/usr/bin/env bash
# 尔雅 Erya — Linux 用户级安装（无需 root）。
# 用法：解压发行包后运行 ./install.sh [安装目录]（默认 ~/.local/share/erya）。
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_DIR="${1:-$DATA_HOME/erya}"

if [[ ! -x "$SRC/Erya" ]]; then
  echo "错误：$SRC 下没有 Erya 可执行文件。请先在仓库根目录运行 packaging/linux/dist.sh 生成发行包，再解压运行本脚本。" >&2
  exit 1
fi

echo "==> 安装程序文件到 $APP_DIR"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR"
# 排除打包脚本自身：程序目录里只要运行时文件。
tar -C "$SRC" \
    --exclude='./install.sh' --exclude='./uninstall.sh' \
    --exclude='./erya.desktop.in' --exclude='./erya.svg' \
    -cf - . | tar -C "$APP_DIR" -xf -

echo "==> 安装桌面入口与图标"
install -Dm644 "$SRC/erya.svg" "$DATA_HOME/icons/hicolor/scalable/apps/erya.svg"
mkdir -p "$DATA_HOME/applications"
sed "s|@INSTALL_DIR@|$APP_DIR|g" "$SRC/erya.desktop.in" > "$DATA_HOME/applications/erya.desktop"

# 桌面数据库刷新是尽力而为：最小系统上工具可能不存在。
update-desktop-database "$DATA_HOME/applications" 2>/dev/null || true

echo
echo "安装完成：从应用菜单启动「尔雅 Erya」，或直接运行 $APP_DIR/Erya"
echo "卸载：$SRC/uninstall.sh（或 $APP_DIR/uninstall.sh）"
