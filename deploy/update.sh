#!/bin/bash
set -euo pipefail

REPO="TomDan-GodsHand/TaTaTask"
INSTALL_DIR="/opt/tatatask"
SERVICE="tatatask.service"

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
