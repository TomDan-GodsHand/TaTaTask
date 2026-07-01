#!/bin/bash

REPO="TomDan-GodsHand/TaTaTask"
INSTALL_DIR="/opt/tatatask"
SERVICE="tatatask.service"
SCRIPT_VERSION=2

# ── 自更新（必须在 set -e 之前执行，避免 pipefail 在赋值中触发退出）──
SELF_URL="https://raw.githubusercontent.com/${REPO}/main/deploy/update.sh"
REMOTE_VER=$(curl -sL "$SELF_URL" 2>/dev/null | grep '^SCRIPT_VERSION=' | cut -d= -f2 || true)

if [ -n "$REMOTE_VER" ] && [ "$REMOTE_VER" -gt "$SCRIPT_VERSION" ] 2>/dev/null; then
    SCRIPT_PATH=$(readlink -f "$0")
    echo "==> update.sh 有新版本 ($SCRIPT_VERSION -> $REMOTE_VER)，先更新自身..."
    curl -sL "$SELF_URL" -o "$SCRIPT_PATH"
    chmod +x "$SCRIPT_PATH"
    exec "$SCRIPT_PATH" "$@"
fi

set -euo pipefail
# ── 自更新结束，以下受 set -e 保护 ──

echo "==> 检查最新版本..."
LATEST=$(curl -sL "https://api.github.com/repos/${REPO}/releases/latest" | grep '"tag_name"' | head -1 | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$LATEST" ]; then
    echo "错误: 无法获取最新版本号"
    exit 1
fi

echo "==> 最新版本: $LATEST"
echo "==> 下载发布包..."
curl -sL "https://github.com/${REPO}/releases/latest/download/tatatask-${LATEST#v}-linux-x64.tar.gz" -o "/tmp/tatatask.tar.gz"

echo "==> 备份当前版本..."
if [ -f "${INSTALL_DIR}/TaTaTask" ]; then
    cp "${INSTALL_DIR}/TaTaTask" "/tmp/TaTaTask.bak" 2>/dev/null || true
fi

echo "==> 备份配置文件..."
for f in appsettings.json appsettings.Production.json; do
    [ -f "${INSTALL_DIR}/${f}" ] && sudo cp "${INSTALL_DIR}/${f}" "/tmp/${f}.bak"
done

echo "==> 停止服务..."
sudo systemctl stop tatatask 2>/dev/null || true

echo "==> 解压安装到 ${INSTALL_DIR}..."
sudo mkdir -p "${INSTALL_DIR}"
sudo tar xzf "/tmp/tatatask.tar.gz" -C "${INSTALL_DIR}"

echo "==> 还原配置文件..."
for f in appsettings.json appsettings.Production.json; do
    [ -f "/tmp/${f}.bak" ] && sudo cp "/tmp/${f}.bak" "${INSTALL_DIR}/${f}"
done

echo "==> 检查运行用户..."
id tatatask &>/dev/null || sudo useradd -r -s /usr/sbin/nologin tatatask

echo "==> 初始化 SSL 目录(首次)..."
sudo mkdir -p "${INSTALL_DIR}/ssl"
sudo touch "${INSTALL_DIR}/ssl/pass.env"
sudo chown -R tatatask:tatatask "${INSTALL_DIR}/ssl"
sudo chmod 700 "${INSTALL_DIR}/ssl"
sudo chmod 600 "${INSTALL_DIR}/ssl/pass.env"

echo "==> 设置权限..."
sudo chmod +x "${INSTALL_DIR}/TaTaTask"
sudo chown -R tatatask:tatatask "${INSTALL_DIR}"

echo "==> 更新数据库结构..."
sudo -u tatatask bash -c "cd ${INSTALL_DIR} && ./TaTaTask --migrate-only"

echo "==> 安装 systemd 服务(首次) 或重载..."
if [ -f "${INSTALL_DIR}/${SERVICE}" ]; then
    sudo cp "${INSTALL_DIR}/${SERVICE}" /usr/lib/systemd/system/
    sudo systemctl daemon-reload
fi

echo "==> 启动服务..."
sudo systemctl start tatatask

echo "==> 检查状态..."
sudo systemctl status tatatask --no-pager -l

echo "==> 更新完成: $LATEST"
rm -f "/tmp/tatatask.tar.gz"
