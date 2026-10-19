#!/usr/bin/env bash
# 尔雅 Erya — 卸载用户级安装。RIME 配置与尔雅自身偏好不受影响。
set -euo pipefail

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_DIR="${1:-$DATA_HOME/erya}"

rm -rf "$APP_DIR"
rm -f "$DATA_HOME/applications/erya.desktop"
rm -f "$DATA_HOME/icons/hicolor/scalable/apps/erya.svg"
update-desktop-database "$DATA_HOME/applications" 2>/dev/null || true

echo "已卸载尔雅。用户目录（RIME 配置）与 ~/.config/Erya（尔雅偏好）保持不变。"
