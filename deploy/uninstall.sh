#!/bin/bash
set -euo pipefail

SERVICE="tatatask"
INSTALL_DIR="/opt/tatatask"
TIMER_SERVICE="tatatask-update.timer"
UPDATE_SERVICE="tatatask-update.service"
UPDATE_SCRIPT="/usr/local/bin/update-tatatask"

echo "==> TaTaTask 卸载脚本"
echo ""
read -rp "确认卸载 TaTaTask？这将删除所有数据和配置。(y/N) " CONFIRM
if [[ "${CONFIRM,,}" != "y" ]]; then
    echo "已取消"
    exit 0
fi

echo ""
echo "==> 停止服务..."
sudo systemctl stop "${SERVICE}" 2>/dev/null || true

echo "==> 停止定时更新..."
sudo systemctl stop "${TIMER_SERVICE}" 2>/dev/null || true
sudo systemctl disable "${TIMER_SERVICE}" 2>/dev/null || true

echo "==> 禁用服务..."
sudo systemctl disable "${SERVICE}" 2>/dev/null || true

echo "==> 删除 systemd 文件..."
sudo rm -f "/usr/lib/systemd/system/${SERVICE}.service"
sudo rm -f "/usr/lib/systemd/system/${UPDATE_SERVICE}"
sudo rm -f "/usr/lib/systemd/system/${TIMER_SERVICE}"
sudo rm -f "/etc/systemd/system/${UPDATE_SERVICE}"
sudo rm -f "/etc/systemd/system/${TIMER_SERVICE}"
sudo systemctl daemon-reload

echo "==> 删除更新脚本..."
sudo rm -f "${UPDATE_SCRIPT}"

echo "==> 删除程序目录 ${INSTALL_DIR}..."
sudo rm -rf "${INSTALL_DIR}"

echo "==> 删除数据库..."
rm -f "${HOME}/.local/share/tatatask/tatatask.db" 2>/dev/null || true

read -rp "是否也删除 tatatask 用户？(y/N) " DELUSER
if [[ "${DELUSER,,}" == "y" ]]; then
    sudo userdel tatatask 2>/dev/null || true
    echo "   用户已删除"
fi

echo ""
echo "==> 卸载完成"
