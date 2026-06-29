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
- [ ] EF Core + SQLite 数据层配置
- [ ] 数据模型定义（User, TodoItem, TodoStep）
- [ ] Cookie 认证基础设施搭建
- [ ] MudBlazor 集成与主题配置

### Phase 2 — 用户系统

- [ ] `/register` 注册页：BCrypt 密码哈希，用户名唯一校验
- [ ] `/login` 登录页：Cookie 认证，失败防用户枚举
- [ ] `/` 首页：自动跳转 `/todos`（已登录）或 `/login`（未登录）
- [ ] 登出功能
- [ ] 用户空间隔离（所有查询带 `UserId` 过滤）

### Phase 3 — 看板核心

- [ ] 看板数据模型与数据库迁移（TodoItem: Title, Description, Priority, Tags, DueDate, DueWarningHours, Status, SortOrder, CreatedAt, UpdatedAt）
- [ ] `/todos` 看板页：5 泳道横向滚动布局 + 临期提醒条
- [ ] 新建任务（任意泳道底部输入）
- [ ] 任务卡片展示（标题、描述、优先级、标签徽章、截止时间着色、流转按钮）
- [ ] 行内编辑（标题、描述、截止时间、优先级、标签、临期阈值）
- [ ] 泳道流转按钮（状态门禁：步骤全部完成方可进已完成；冻结→解冻到未开始）
- [ ] 删除任务（确认后硬删除）
- [ ] 拖拽泳道间移动（受流转约束）
- [ ] 泳道内排序（截止时间加权降序 → 优先级降序 → SortOrder 升序）
- [ ] 临期提醒条（横向顶部通栏，阈值默认 48h 可自定义，最多 8 条，超 7 天过期自动移除）
- [ ] 看板搜索框（标题 + 标签实时过滤，URL 参数 `?q=`）

### Phase 4 — 子步骤

- [ ] 子步骤数据模型与迁移
- [ ] 展开/折叠步骤列表
- [ ] 动态增删子步骤
- [ ] Checkbox 切换完成状态
- [ ] 步骤完成门禁（全部完成方可流转至已完成泳道）
- [ ] 子步骤排序

### Phase 5 — 归档系统

- [ ] 归档操作（仅"已完成"泳道任务，不可逆）
- [ ] 自动归档（已完成超 7 天自动归档）
- [ ] `/archive` 归档浏览页（按归档时间倒序）
- [ ] 筛选：标签模糊匹配、日期范围、标题关键词搜索

### Phase 6 — 交付打磨

- [ ] 错误处理与异常页面
- [ ] 前端表单验证
- [ ] 响应式适配
- [ ] `linux-x64` 发布与部署文档

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
