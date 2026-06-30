# TaTaTask

自托管的看板式 TODO 管理 Web 应用，面向个人使用。

## 技术栈

| 项目     | 选型                              |
|----------|-----------------------------------|
| 运行时   | .NET 10                           |
| 前端     | Blazor + MudBlazor                |
| 渲染模式 | InteractiveAuto / SSR             |
| 数据库   | SQLite (EF Core)                  |
| 认证     | ASP.NET Core Cookie Auth + BCrypt |
| 部署     | 单进程自托管，linux-x64           |

## 功能概览

- **用户系统**：自由注册/登录，Cookie 认证（14天），用户空间完全隔离
- **看板**：5 个预设泳道（未开始 / 进行中 / 收敛中 / 已完成 / 冻结），横向滚动，支持标题/标签实时搜索
- **任务**：标题、描述、优先级（! / !! / !!!）、标签徽章、截止时间（过期/今天/未来 着色）、泳道流转按钮、拖拽移动
- **子步骤**：任务内多步骤，展开折叠、勾选完成、独立排序
- **临期提醒**：看板顶部提醒条（最多 8 条），距截止时间 < 自定义阈值的任务自动置顶，过期超 7 天自动移除、可临时关闭
- **归档**：已完成任务一键/自动（7天）归档，不可逆，支持标签/日期/标题历史浏览

## 页面

| 路由        | 页面     | 渲染模式          | 认证   |
|-------------|----------|-------------------|--------|
| `/`         | 首页     | InteractiveServer | 否     |
| `/login`    | 登录     | SSR               | 否     |
| `/register` | 注册     | SSR               | 否     |
| `/todos`    | 看板     | InteractiveAuto   | 是（`?q=`搜索） |
| `/archive`  | 归档浏览 | InteractiveAuto   | 是     |

## Roadmap

### Phase 1 — 基础设施

- [x] .NET 10 Blazor Web App 项目搭建
- [x] EF Core + SQLite 数据层配置
- [x] 数据模型定义（User, TodoItem, TodoStep）
- [x] Cookie 认证基础设施搭建
- [x] MudBlazor 集成与主题配置

### Phase 2 — 用户系统

- [x] `/register` 注册页：BCrypt 密码哈希，用户名唯一校验
- [x] `/login` 登录页：Cookie 认证，失败防用户枚举
- [x] `/` 首页：自动跳转 `/todos`（已登录）或 `/login`（未登录）
- [x] 登出功能
- [x] 用户空间隔离（所有查询带 `UserId` 过滤）

### Phase 3 — 看板核心

- [x] 看板数据模型与数据库迁移（TodoItem: Title, Description, Priority, Tags, DueDate, DueWarningHours, Status, SortOrder, CreatedAt, UpdatedAt）
- [x] `/todos` 看板页：5 泳道横向滚动布局 + 临期提醒条
- [x] 新建任务（任意泳道底部输入）
- [x] 任务卡片展示（标题、描述、优先级、标签徽章、截止时间着色、流转按钮）
- [x] 行内编辑（标题、描述、截止时间、优先级、标签、临期阈值）
- [x] 泳道流转按钮（状态门禁：步骤全部完成方可进已完成；冻结→解冻到未开始）
- [x] 删除任务（确认后硬删除）
- [x] 拖拽泳道间移动（受流转约束）
- [x] 泳道内排序（截止时间加权降序 → 优先级降序 → SortOrder 升序）
- [x] 临期提醒条（横向顶部通栏，阈值默认 48h 可自定义，最多 8 条，超 7 天过期自动移除）
- [x] 看板搜索框（标题 + 标签实时过滤，URL 参数 `?q=`）

### Phase 4 — 子步骤

- [x] 子步骤数据模型与迁移
- [x] 展开/折叠步骤列表
- [x] 动态增删子步骤
- [x] Checkbox 切换完成状态
- [x] 步骤完成门禁（全部完成方可流转至已完成泳道）
- [x] 子步骤排序

### Phase 5 — 归档系统

- [x] 归档操作（仅"已完成"泳道任务，不可逆）
- [x] 自动归档（已完成超 7 天自动归档）
- [x] `/archive` 归档浏览页（按归档时间倒序）
- [x] 筛选：标签模糊匹配、日期范围、标题关键词搜索

### Phase 6 — 交付打磨

- [ ] 错误处理与异常页面
- [ ] 前端表单验证
- [ ] 响应式适配
- [x] `linux-x64` 发布与部署文档

## 项目结构

```
TaTaTask/
├── TaTaTask.slnx                       # 解决方案文件
├── requirements.md                     # 需求说明书
├── README.md
│
├── TaTaTask/                           # ASP.NET Core 宿主（Server）
│   ├── TaTaTask.csproj                 #   引用 Client + Models
│   ├── Program.cs                      #   入口：认证、路由、WASM 调试
│   ├── Components/
│   │   ├── App.razor                   #   HTML 壳（<head> + <body>）
│   │   ├── Routes.razor                #   Blazor Router
│   │   ├── _Imports.razor
│   │   ├── Layout/
│   │   │   ├── MainLayout.razor/.css   #   主布局（侧栏 + 内容区）
│   │   │   ├── NavMenu.razor/.css      #   导航菜单
│   │   │   └── ReconnectModal.razor/   #   Blazor Server 重连弹窗
│   │   └── Pages/
│   │       ├── Home.razor              #   /      首页
│   │       ├── Error.razor             #   /Error 错误页
│   │       └── NotFound.razor          #   404 页
│   ├── Properties/launchSettings.json
│   └── wwwroot/
│       ├── app.css
│       └── lib/bootstrap/              #   静态 Bootstrap 资源
│
├── TaTaTask.Client/                    # Blazor WebAssembly 组件
│   ├── TaTaTask.Client.csproj          #   引用 Models
│   ├── Program.cs                      #   WASM 启动入口
│   ├── _Imports.razor
│   └── Pages/
│       └── Counter.razor               #   /counter 示例页（将替换为 /todos）
│
└── TaTaTask.Models/                    # 共享类型库（Server ↔ Client）
    ├── TaTaTask.Models.csproj
    ├── Enums/
    │   └── TodoStatus.cs               # 泳道枚举
    ├── Entities/
    │   ├── User.cs                     # 用户实体
    │   ├── TodoItem.cs                 # 任务实体
    │   └── TodoStep.cs                 # 子步骤实体
    └── Dtos/
        ├── LoginRequest.cs
        ├── RegisterRequest.cs
        └── TodoItemDto.cs
```

### 依赖关系

```
TaTaTask (Server)       引用 → TaTaTask.Client
       │                          │
       └──────── 引用 ────────────┘
                  ↓
           TaTaTask.Models (共享类型)
```

- **Server**：承载 SSR 页面（登录/注册）、API、EF Core 数据层，通过 `AddAdditionalAssemblies` 发现 Client 的 InteractiveAuto 组件
- **Client**：WASM 组件，看板、归档等交互页面在此实现
- **Models**：实体、枚举、DTO，Server 和 Client 共同引用，避免循环依赖

## 快速开始

```bash
# 克隆仓库
git clone <repo-url> && cd TaTaTask

# 安装 EF Core 工具
dotnet tool restore

# 运行数据库迁移
dotnet ef database update

# 启动开发服务器
dotnet run --project TaTaTask
```

打开 `https://localhost:7162` 即可访问。

## 发布（GitHub Actions 自动构建）

基于 git tag 驱动，推送 tag 自动触发 CI/CD 构建并创建 GitHub Release：

```bash
git tag v1.0.0
git push origin v1.0.0
```

工作流（`.github/workflows/release.yml`）：
1. 安装 .NET 10 SDK
2. `dotnet publish` → `linux-x64` 自包含单文件
3. 打包为 `tatatask-{version}-linux-x64.tar.gz`
4. 创建 GitHub Release 并附带构建产物

版本号写入程序集（`TaTaTask.csproj` → `<Version>`），可在设置页面底部查看当前运行版本。

---

## 部署到 systemd（linux-x64）

### 方式一：手动部署

```bash
dotnet publish TaTaTask -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish
```

```bash
sudo mkdir -p /opt/tatatask/ssl
sudo cp your-cert.pfx /opt/tatatask/ssl/tatatask.pfx
sudo useradd -r -s /usr/sbin/nologin tatatask   # 首次运行只需执行一次
sudo chown -R tatatask:tatatask /opt/tatatask
sudo chmod 700 /opt/tatatask/ssl
sudo chmod 600 /opt/tatatask/ssl/tatatask.pfx
sudo cp deploy/tatatask.service /usr/lib/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now tatatask
```

### 方式二：一键部署/更新脚本（Arch Linux 推荐）

首次部署：

```bash
curl -sL https://raw.githubusercontent.com/TomDan-GodsHand/TaTaTask/main/deploy/update.sh -o update.sh
chmod +x update.sh
./update.sh
```

之后每次发布新版本，只需一行：

```bash
./update.sh
```

`update.sh` 自动完成：检查最新 Release → 下载 `tar.gz` → 停止服务 → 解压到 `/opt/tatatask` → 安装 systemd 服务 → 启动。

#### 定时自动更新（可选）

```bash
# /etc/systemd/system/tatatask-update.service
[Unit]
Description=Update TaTaTask

[Service]
Type=oneshot
ExecStart=/usr/local/bin/update-tatatask
```

```bash
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

### 配置 HTTPS/SSL

Kestrel 端口和证书通过 `appsettings.json` 配置（运行时生效），默认监听 HTTP:5000 + HTTPS:5001。

**1. 部署证书（首次）**

```bash
# PFX 格式（推荐，含证书+私钥，单文件）
sudo cp your-cert.pfx /opt/tatatask/ssl/tatatask.pfx
sudo chown tatatask:tatatask /opt/tatatask/ssl/tatatask.pfx
sudo chmod 600 /opt/tatatask/ssl/tatatask.pfx

# 如果证书有密码，修改 appsettings.json:
# "Certificate": { "Path": "ssl/tatatask.pfx", "Password": "你的密码" }
```

**2. 仅 HTTP（不需要 HTTPS）**

注释掉或删除 `appsettings.json` 里的 `Https` 节点，systemd service 里取消注释 `ASPNETCORE_URLS`。

**3. 仅 HTTPS（强制）**

删除 `Http` 节点，只保留 `Https`。

**4. 自定义端口**

修改 `appsettings.json` 里的端口号，或 systemd service 里设置 `ASPNETCORE_URLS` 环境变量（优先级高于配置）。

> 证书 `ssl/` 目录不在发布包中，更新不会覆盖已有证书。

### 日志

`TaTaTask` 通过 `Host.UseSystemd()` 与 systemd 集成：`Type=notify` 就绪通知、`SIGTERM` 优雅停止，日志自动对接 systemd journal（`StandardOutput=journal`）。

```bash
journalctl -u tatatask -f
```

### 卸载

```bash
curl -sL https://raw.githubusercontent.com/TomDan-GodsHand/TaTaTask/main/deploy/uninstall.sh -o uninstall.sh
chmod +x uninstall.sh
./uninstall.sh
```

脚本会依次：停止服务 → 禁用 systemd → 删除程序目录 `/opt/tatatask` → 删除数据库 → 可选删除 `tatatask` 用户。
