<p align="center">
  <img src="TaTaTask/wwwroot/img/logo.png" alt="TaTaTask" height="80" />
</p>

<h1 align="center">TaTaTask</h1>

<p align="center">
  <strong>自托管的看板式个人任务管理应用</strong><br>
  简单、快速、隐私优先。你的任务你做主。
</p>

<p align="center">
  <a href="https://github.com/TomDan-GodsHand/TaTaTask/blob/main/LICENSE.txt"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License" /></a>
  <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0"><img src="https://img.shields.io/badge/.NET-10.0-512bd4?logo=dotnet" alt=".NET 10" /></a>
  <a href="https://github.com/TomDan-GodsHand/TaTaTask/actions"><img src="https://img.shields.io/badge/build-passing-brightgreen.svg" alt="Build" /></a>
  <img src="https://img.shields.io/badge/platform-linux--x64-blueviolet?logo=linux" alt="Platform" />
  <img src="https://img.shields.io/badge/database-SQLite-003b57?logo=sqlite" alt="SQLite" />
</p>

---

## 特性

<table>
<tr><td width="50%">

### 看板管理
- **4 个预设泳道** — 未开始 / 进行中 / 已完成 / 冻结
- **泳道流转** — 一键推进任务，拦截规则保护流程
- **拖拽排序** — 截止时间 + 优先级智能加权排序
- **实时搜索** — 标题 + 标签关键词过滤，URL 参数持久化

### 任务管理
- 标题、描述、优先级（! / !! / !!! / !!!!）
- 标签徽章，逗号分隔多标签
- 截止时间着色（蓝色=未来 / 橙色=今天 / 红色=已逾期）
- 行内编辑，所见即所得

</td><td width="50%">

### 子步骤
- 任务内多步骤，展开/折叠
- Checkbox 勾选完成，完成率实时显示
- 全部完成方可流转至「已完成」门禁

### 冻结机制
- 暂停推进的任务移入「冰箱」泳道
- 弹窗输入冻结原因，记录冻结时长
- 解冻自动恢复到进入前的原状态
- 雪花水印背景（浅蓝冰感主题）

### 撤回与确认
- 进行中可撤回至未开始（子步骤全部重置）
- 已完成可撤回至进行中
- 开始、完成、删除、归档、冻结全部使用 MudBlazor 风格确认弹窗

### 实时同步
- SignalR WebSocket 推送，多标签/多设备数据自动同步
- 一个标签操作，其他标签即时刷新

### 临期提醒
- 看板顶部通栏，距截止时间 < 阈值自动提醒
- 过期 7 天自动移除，可单独关闭

### 归档系统
- 已完成任务一键归档，7 天自动归档
- 按标签、日期范围、标题关键词浏览历史

### 数据统计
- 顶部指标卡：活跃 / 今日完成 / 冻结 / 逾期 / 步骤完成率
- 冻结清单：按时长排列，≥7 天红色警告
- 近 7 天完成趋势柱状图

</td></tr>
</table>

---

## 技术栈

| 类别     | 选型                                       |
| -------- | ------------------------------------------ |
| 运行时   | .NET 10                                    |
| 前端框架 | Blazor (InteractiveAuto + SSR)             |
| UI 组件  | [MudBlazor](https://mudblazor.com/)         |
| 实时通信 | SignalR (WebSocket)                        |
| 数据库   | SQLite (EF Core Code-First)                |
| 认证     | ASP.NET Core Cookie Auth + BCrypt 密码哈希 |
| 部署     | 单进程自托管，linux-x64，systemd 管理      |
| CI/CD    | GitHub Actions，tag 驱动自动构建 Release   |

---

## 快速开始

### 前置条件

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

### 开发运行

```bash
git clone https://github.com/TomDan-GodsHand/TaTaTask.git
cd TaTaTask

# 安装 EF Core 工具
dotnet tool restore

# 初始化数据库
dotnet ef database update --project TaTaTask

# 启动开发服务器
dotnet run --project TaTaTask
```

打开 `https://localhost:5001`（或 HTTP `http://localhost:5000`）。

---

## 部署到生产环境 (linux-x64)

### 一键更新脚本（推荐）

```bash
curl -sL https://raw.githubusercontent.com/TomDan-GodsHand/TaTaTask/main/deploy/update.sh -o update.sh
chmod +x update.sh
sudo ./update.sh
```

`update.sh` 自动完成：

1. **自更新检查** — 脚本先检查组自身是否有新版本
2. 下载最新 GitHub Release 压缩包
3. 备份当前版本和配置文件
4. 停止服务 → 解压 → 还原配置
5. 运行数据库迁移 (`--migrate-only`)
6. 设置权限 → 安装/重载 systemd → 启动服务

以后每次发新版只需 `sudo ./update.sh`。

### 手动部署

```bash
dotnet publish TaTaTask -c Release -r linux-x64 --self-contained -o publish

sudo mkdir -p /opt/tatatask/ssl
sudo cp -r publish/* /opt/tatatask/
sudo useradd -r -s /usr/sbin/nologin tatatask
sudo chown -R tatatask:tatatask /opt/tatatask
sudo chmod +x /opt/tatatask/TaTaTask

sudo cp deploy/tatatask.service /usr/lib/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now tatatask
```

### 定时自动更新

```ini
# /etc/systemd/system/tatatask-update.service
[Unit]
Description=Update TaTaTask
[Service]
Type=oneshot
ExecStart=/usr/local/bin/update-tatatask

# /etc/systemd/system/tatatask-update.timer
[Unit]
Description=Weekly TaTaTask update check
[Timer]
OnCalendar=weekly
Persistent=true
[Install]
WantedBy=timers.target
```

```bash
sudo cp update.sh /usr/local/bin/update-tatatask
sudo systemctl enable --now tatatask-update.timer
```

### 日志

```bash
journalctl -u tatatask -f
```

### 卸载

```bash
curl -sL https://raw.githubusercontent.com/TomDan-GodsHand/TaTaTask/main/deploy/uninstall.sh -o uninstall.sh
chmod +x uninstall.sh
./uninstall.sh
```

### 反向代理

生产环境建议 nginx/Caddy 反代，应用只监听 HTTP。

**1. 在 `appsettings.Production.json` 中开启反代模式：**

```json
{
  "ReverseProxy": true,
  "Kestrel": {
    "Endpoints": {
      "Https": null
    }
  }
}
```

- `ReverseProxy: true` → 信任 `X-Forwarded-*` 头，禁用 HTTPS 重定向
- `Https: null` → Kestrel 不再监听 HTTPS（证书由反代层处理）

**2. nginx 反代配置示例：**

```nginx
server {
    listen 443 ssl http2;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SignalR WebSocket 升级
    location /hubs/ {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 项目结构

```
TaTaTask/
├── TaTaTask.slnx
├── .github/workflows/release.yml     # CI/CD 自动构建
├── deploy/
│   ├── update.sh                     # 一键部署/更新脚本
│   ├── uninstall.sh                  # 卸载脚本
│   └── tatatask.service              # systemd 单元文件
│
├── TaTaTask/                         # ASP.NET Core 服务端
│   ├── Program.cs                    # 入口：认证、路由、迁移
│   ├── Services/
│   │   └── ServerTodoService.cs      # 业务逻辑 + EF Core 查询
│   ├── Controllers/
│   │   ├── TodoController.cs         # 任务 CRUD API
│   │   ├── StatsController.cs        # 统计数据 API
│   │   ├── ArchiveController.cs      # 归档 API
│   │   └── UserController.cs         # 用户 API
│   ├── Hubs/
│   │   └── TodoHub.cs                # SignalR 实时推送 Hub
│   ├── Data/AppDbContext.cs          # EF Core DbContext
│   ├── Migrations/                   # 数据库迁移文件
│   └── wwwroot/app.css               # 全局样式
│
├── TaTaTask.Client/                  # Blazor WebAssembly 组件
│   ├── Services/
│   │   ├── ITodoService.cs           # 服务接口
│   │   └── ClientTodoService.cs      # HTTP 客户端实现
│   └── Pages/
│       ├── Todos.razor               # 看板页面
│       ├── Archive.razor             # 归档浏览
│       └── Stats.razor               # 数据统计
│
└── TaTaTask.Models/                  # 共享类型库
    ├── Entities/                     # 数据库实体
    ├── Dtos/                         # 请求/响应模型
    └── Enums/TodoStatus.cs           # 泳道状态枚举
```

---

## 发布流程

推送版本标签触发 GitHub Actions 自动构建：

```bash
git tag v1.2.0
git push origin v1.2.0
```

工作流自动完成：
1. `dotnet publish` → `linux-x64` 自包含发布
2. 打包 `tatatask-{version}-linux-x64.tar.gz`（含 systemd service 文件）
3. 创建 GitHub Release 附带构建产物

版本号写入程序集，可在设置页面底部查看。

---

## Roadmap

- [x] 用户注册/登录（Cookie + BCrypt）
- [x] 看板泳道管理（未开始 → 进行中 → 已完成）
- [x] 任务 CRUD + 行内编辑
- [x] 优先级、标签、截止时间
- [x] 子步骤（增删 + 勾选完成 + 门禁）
- [x] 冻结机制（原因记录 + 时长追踪 + 原状态恢复）
- [x] 临期提醒 + 过期自动清理
- [x] 归档系统（手动 + 自动 + 历史浏览）
- [x] 数据统计大盘（指标卡 + 冻结清单 + 完成趋势）
- [x] 统一确认弹窗（MudBlazor 风格，替代 JS confirm）
- [x] SignalR 实时推送（多标签/多设备同步）
- [x] 撤回机制（进行中↩未开始、已完成↩进行中）
- [x] 反代支持（纯 HTTP + ForwardedHeaders）
- [x] `linux-x64` 一键部署脚本（自更新 + 数据库迁移）
- [ ] 移动端响应式适配
- [ ] 深色模式
- [ ] Docker 镜像

---

## 贡献

欢迎提交 Issue 和 Pull Request。

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/amazing-feature`)
3. 提交改动 (`git commit -m 'Add amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 创建 Pull Request

---

## 许可

[MIT License](LICENSE.txt)
