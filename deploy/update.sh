#!/bin/bash

REPO="TomDan-GodsHand/TaTaTask"
INSTALL_DIR="/opt/tatatask"
SERVICE="tatatask.service"
SCRIPT_VERSION=5

# ── 辅助函数 ──
die() {
    echo "错误: $1" >&2
    exit 1
}
step() {
    echo "==> $1"
}

# ── 自更新 ──
SELF_URL="https://raw.githubusercontent.com/${REPO}/master/deploy/update.sh"
echo "==> 检查脚本自更新..."
REMOTE_VER=$(curl -sL --retry 2 --retry-delay 3 --connect-timeout 15 "$SELF_URL" 2>/dev/null | grep '^SCRIPT_VERSION=' | cut -d= -f2 || true)

if [ -z "$REMOTE_VER" ]; then
    echo "    警告: 无法连接到 GitHub 检查脚本更新，跳过"
elif [ "$REMOTE_VER" -gt "$SCRIPT_VERSION" ] 2>/dev/null; then
    SCRIPT_PATH=$(readlink -f "$0" 2>/dev/null || echo "$0")
    echo "==> update.sh 有新版本 ($SCRIPT_VERSION -> $REMOTE_VER)，正在更新..."
    if curl -L --retry 2 --retry-delay 3 --connect-timeout 30 "$SELF_URL" -o "$SCRIPT_PATH"; then
        chmod +x "$SCRIPT_PATH" && exec "$SCRIPT_PATH" "$@"
    fi
    echo "    警告: 脚本自更新下载失败，继续使用当前版本"
fi

# ── 检查最新版本 ──
step "检查最新版本..."
LATEST=$(curl -sL "https://api.github.com/repos/${REPO}/releases/latest" | grep '"tag_name"' | head -1 | sed -E 's/.*"([^"]+)".*/\1/')
if [ -z "$LATEST" ]; then
    die "无法获取最新版本号，请检查网络连接"
fi
echo "    最新版本: $LATEST"

VERSION_FILE="${INSTALL_DIR}/VERSION"
if [ -f "$VERSION_FILE" ]; then
    CURRENT=$(cat "$VERSION_FILE")
    if [ "$CURRENT" = "$LATEST" ]; then
        echo "    已是最新版本: $LATEST，无需更新"
        exit 0
    fi
    echo "    当前版本: $CURRENT -> 最新: $LATEST"
fi

# ── 下载发布包 ──
step "下载发布包..."
DOWNLOAD_URL="https://github.com/${REPO}/releases/latest/download/tatatask-${LATEST#v}-linux-x64.tar.gz"
curl -L --progress-bar --retry 3 --retry-delay 5 --retry-max-time 120 --connect-timeout 30 "$DOWNLOAD_URL" -o "/tmp/tatatask.tar.gz" || die "下载发布包失败 ($DOWNLOAD_URL)"

# ── 备份当前版本 ──
step "备份当前版本..."
if [ -f "${INSTALL_DIR}/TaTaTask" ]; then
    cp "${INSTALL_DIR}/TaTaTask" "/tmp/TaTaTask.bak" 2>/dev/null || echo "    警告: 备份当前版本失败，继续..."
fi

# ── 备份配置文件 ──
step "备份配置文件..."
for f in appsettings.json appsettings.Production.json; do
    if [ -f "${INSTALL_DIR}/${f}" ]; then
        sudo cp "${INSTALL_DIR}/${f}" "/tmp/${f}.bak" || echo "    警告: 备份 ${f} 失败，继续..."
    fi
done

# ── 停止服务 ──
step "停止服务..."
sudo systemctl stop tatatask 2>/dev/null || echo "    提示: 服务未运行或停止失败，继续..."

# ── 解压安装 ──
step "解压安装到 ${INSTALL_DIR}..."
sudo mkdir -p "${INSTALL_DIR}" || die "无法创建安装目录 ${INSTALL_DIR}"
sudo tar xzf "/tmp/tatatask.tar.gz" -C "${INSTALL_DIR}" || die "解压失败，可能下载的文件已损坏"

# ── 还原配置文件 ──
step "还原配置文件..."
for f in appsettings.json appsettings.Production.json; do
    if [ -f "/tmp/${f}.bak" ]; then
        sudo cp "/tmp/${f}.bak" "${INSTALL_DIR}/${f}" || echo "    警告: 还原 ${f} 失败，继续..."
    fi
done

# ── 检查运行用户 ──
step "检查运行用户..."
id tatatask &>/dev/null || sudo useradd -r -s /usr/sbin/nologin tatatask || die "创建运行用户 tatatask 失败"

# ── 初始化 SSL 目录 ──
step "初始化 SSL 目录（首次）..."
sudo mkdir -p "${INSTALL_DIR}/ssl" || die "创建 SSL 目录 ${INSTALL_DIR}/ssl 失败"
sudo touch "${INSTALL_DIR}/ssl/pass.env" || die "创建 SSL 密码文件失败"
sudo chown -R tatatask:tatatask "${INSTALL_DIR}/ssl" || die "设置 SSL 目录所有者失败"
sudo chmod 700 "${INSTALL_DIR}/ssl" || die "设置 SSL 目录权限失败"
sudo chmod 600 "${INSTALL_DIR}/ssl/pass.env" || die "设置 SSL 密码文件权限失败"

# ── 设置权限 ──
step "设置权限..."
sudo chmod +x "${INSTALL_DIR}/TaTaTask" || die "设置可执行权限失败"
sudo chown -R tatatask:tatatask "${INSTALL_DIR}" || die "设置目录所有者失败"

# ── 数据库迁移 ──
step "更新数据库结构..."
sudo -u tatatask bash -c "cd ${INSTALL_DIR} && ./TaTaTask --migrate-only" || die "数据库迁移失败，请检查日志"

# ── 安装 systemd 服务 ──
step "安装 systemd 服务（首次）或重载..."
if [ -f "${INSTALL_DIR}/${SERVICE}" ]; then
    sudo cp "${INSTALL_DIR}/${SERVICE}" /usr/lib/systemd/system/ || die "复制 systemd 服务文件失败"
    sudo systemctl daemon-reload || die "重载 systemd 失败"
fi

# ── 启动服务 ──
step "启动服务..."
sudo systemctl start tatatask || die "启动服务失败，请运行 'sudo journalctl -u tatatask -n 50' 查看日志"

# ── 检查状态 ──
step "检查状态..."
sudo systemctl status tatatask --no-pager -l || echo "    警告: 获取服务状态失败"

# ── 写入版本号 ──
step "更新版本记录..."
echo "$LATEST" | sudo tee "${INSTALL_DIR}/VERSION" > /dev/null || die "写入版本号失败"

# ── 清理 ──
rm -f "/tmp/tatatask.tar.gz" "/tmp/TaTaTask.bak" "/tmp/appsettings.json.bak" "/tmp/appsettings.Production.json.bak"
echo "==> 更新完成: $LATEST"
