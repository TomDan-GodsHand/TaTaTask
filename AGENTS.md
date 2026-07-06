# AGENTS.md

## 项目概览

TaTaTask 是一个自托管的看板式个人任务管理应用：.NET 10 Blazor Web App (InteractiveAuto = SSR + WASM)，MudBlazor UI，SQLite (EF Core)，Cookie 认证 + BCrypt，SignalR 实时同步。

## 构建与运行

```bash
dotnet tool restore                                    # 安装 dotnet-ef
dotnet ef database update --project TaTaTask           # 创建/迁移 SQLite 数据库
dotnet run --project TaTaTask                          # 开发服务器 (http://localhost:5000, https://localhost:5001)
dotnet build                                           # 构建全部三个项目
dotnet publish TaTaTask -c Release -r linux-x64 --self-contained -o publish
```

本仓库无测试。

## 架构（3 个项目）

| 项目 | 职责 |
|---|---|
| `TaTaTask/` | ASP.NET Core 服务端：控制器、SignalR Hub、EF Core、SSR 组件 |
| `TaTaTask.Client/` | Blazor WASM 客户端：页面，基于 HTTP 的 `ClientTodoService` |
| `TaTaTask.Models/` | 共享实体、DTO、枚举 — 零外部依赖 |

解决方案文件为 `.slnx`（新 VS 格式），不是 `.sln`。

## 关键架构事实

- **`ITodoService` 定义在 `TaTaTask.Client/Services/`** 但两端各有实现：`ServerTodoService`（EF Core，服务端 DI）和 `ClientTodoService`（HTTP 调用，WASM DI）。不要移动这个接口。
- **SQLite 启动时自动迁移**（`Program.cs` 中 `db.Database.Migrate()`）。运行时无需手动迁移。
- **`--migrate-only`** 命令行标志：执行迁移后退出。部署脚本中使用。
- **数据库文件**（`tatatask.db`、`*.db-shm`、`*.db-wal`）已被 gitignore。
- **SignalR Hub** 路径 `/hubs/todo` — 需要 `[Authorize]`。按 `user_{userId}` 分组。客户端调用 `NotifyChange()` 触发其他标签页 `Refresh`。
- **认证**：Cookie 方式（14 天滑动过期）。服务端通过 `PersistentComponentState` 持久化 `UserInfo` → WASM 端通过 `PersistentAuthenticationStateProvider` 读取。所有路由受 `AuthorizeRouteView` 保护。
- **反向代理**模式：在 appsettings 中设置 `"ReverseProxy": true` 以启用 `ForwardedHeaders` 并禁用 HTTPS 重定向。
- **systemd** 集成：`builder.Host.UseSystemd()`。在 Windows/开发环境下无操作。

## 数据库 / 迁移

```bash
dotnet ef migrations add <名称> --project TaTaTask
```

EF Core 工具版本固定在 `.config/dotnet-tools.json`（当前为 `dotnet-ef` 10.0.9）。

五张表：`Users`、`TodoItems`、`TodoSteps`、`FeedbackItems`、`FeedbackReplies`。

## 业务规则（不显而易见）

- 有未完成步骤时不能流转到「已完成」（步骤门禁）。
- 冻结储存 `PreviousStatus` + `FrozenReason` + `FrozeAt`；解冻时恢复到原始状态。
- 自动归档：看板查询时将已完成超过 7 天的任务自动归档。
- 看板排序：有截止时间的任务按优先级加权紧急度评分排序；无截止时间的按优先级 + `SortOrder` 排序。
- 勾选第一个步骤会自动将状态从 `NotStarted` 推进到 `InProgress`。

## CI/CD

GitHub Actions (`release.yml`)：推送 `v*` 标签 → 构建 linux-x64 自包含发布 → 创建 GitHub Release 附 tar.gz。

## 部署

部署脚本位于 `deploy/`：`update.sh`、`uninstall.sh`、`tatatask.service`。目标路径：`/opt/tatatask`。

## 代码风格

`.editorconfig`：4 空格缩进，CRLF 换行。无单独的 linter/formatter 配置。
