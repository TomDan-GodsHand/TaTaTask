# 外部网页与 AI agent 接入

TaTaTask 提供同一应用内的两个入口，共用用户隔离、业务校验与时间换算：

- REST API：`https://tasks.example.com/api/v1`
- MCP：`https://tasks.example.com/mcp`，使用 Streamable HTTP，无状态，每次请求都验证令牌。
- OpenAPI：`https://tasks.example.com/openapi/external.json`，同样需要 Bearer 令牌。

## 创建与撤销令牌

登录网页，在右上角用户菜单打开「外部接入」（`/integrations`），或从设置页进入。填写名称、有效期（1–365 天），按需启用写入权限。每个客户端建议使用独立令牌。

完整令牌只在创建成功后显示一次。数据库仅存 SHA-256 摘要和展示前缀。只读令牌默认启用，读写令牌允许增删改。令牌只能访问所属用户的数据，不能访问账号设置、管理功能，也不能创建新令牌。最多保留 50 个有效令牌。

访问令牌必须通过请求头发送：

```http
Authorization: Bearer <your-token>
```

不支持 URL 查询参数携带令牌。网页登录继续使用 Cookie；外部接口只接受令牌。在「外部接入」撤销令牌后，后续请求立即失效。已在执行的请求不会被中断。

部署到公网时使用 HTTPS。已有反向代理部署方式不变；新版本启动时会自动迁移并创建 `ApiTokens` 表。

## 其他网页

网页有自己的后端时，建议由后端保存令牌并调用 TaTaTask。不要把令牌打包进公开的 JavaScript、Git 仓库或共享网页。

若需要浏览器直接跨域访问，管理员在 `appsettings.json` 中配置精确来源（协议、域名、端口，无路径和末尾斜杠），然后重启：

```json
{
  "ExternalAccess": {
    "AllowedOrigins": ["https://portal.example.com", "http://localhost:5173"]
  }
}
```

默认不允许跨站浏览器访问。也可用环境变量 `ExternalAccess__AllowedOrigins__0=https://portal.example.com`。只对新 API、MCP 和 OpenAPI 应用此 CORS 策略，不允许跨域携带 Cookie。非浏览器的后台脚本和 agent 无需 CORS；MCP 请求如果携带 Origin，必须为同源或在允许列表内。

下面的 `token` 应由当前使用者在运行时提供，不能硬编码到公开页面：

```js
const response = await fetch(`${baseUrl}/api/v1/tasks?page=1&pageSize=20&status=2`, {
  headers: { Authorization: `Bearer ${token}` }
});
if (!response.ok) throw await response.json();
const { items, total, page, pageSize, timeZoneId } = await response.json();
```

## Codex

在运行 Codex 的环境里设置 `TATATASK_TOKEN` 环境变量，并在 Codex 的 `config.toml` 添加：

```toml
[mcp_servers.tatatask]
url = "https://tasks.example.com/mcp"
bearer_token_env_var = "TATATASK_TOKEN"
```

此处填写实际服务域名。令牌放在环境变量中，避免把明文写入配置文件。需要让启动 Codex 的进程能读取该环境变量。

官方配置说明：<https://developers.openai.com/codex/mcp/>。

## dsh 与其他 agent

在客户端的 MCP 配置中选择 **Streamable HTTP / HTTP**，URL 填 `/mcp` 的完整地址，认证头设置为 `Authorization: Bearer <your-token>`。配置文件字段名依客户端而异；支持环境变量替换时，使用客户端的变量替换机制。

本服务使用手动配置的个人访问令牌，未实现 OAuth 自动授权发现。客户端需支持 Bearer 或自定义请求头；仅支持 stdio、旧版 SSE 或只接受 OAuth 登录的客户端需要额外适配。这里未假定 dsh 的具体配置格式。

MCP 暴露以下工具（名称与参数可通过 `tools/list` 发现）：

| 范围 | 工具 |
|---|---|
| 用户与查询 | `get_profile`、`list_tasks`、`get_task` |
| 任务写入 | `create_task`、`update_task`、`delete_task` |
| 任务流转 | `change_task_status`、`unfreeze_task`、`archive_task` |
| 步骤 | `add_task_step`、`update_task_step`、`delete_task_step`、`set_task_step_completion` |
| 日程 | `get_today_schedule`、`list_schedule_rules`、`get_schedule_rule` |
| 规则写入 | `create_schedule_rule`、`update_schedule_rule`、`delete_schedule_rule` |

读写权限由服务端在每次调用时检查；MCP 工具注解仅用于帮助客户端呈现操作性质。工具成功时返回 JSON 文本与 `structuredContent`；业务错误返回 `isError: true`，内容含 `status` 与 `title`。

## REST 路由

所有路由均在 `/api/v1` 下。GET 只需读取权限，其他方法需要写入权限。

| 方法与路径 | 功能 |
|---|---|
| `GET /me` | 用户名、用户时区、当前令牌能否写入 |
| `GET /tasks` | 分页筛选任务 |
| `GET /tasks/{id}` | 含步骤的任务详情 |
| `POST /tasks` | 创建任务，返回 201 与 Location |
| `PUT /tasks/{id}` | 替换可编辑字段 |
| `DELETE /tasks/{id}` | 永久删除任务，成功返回 204 |
| `PUT /tasks/{id}/status` | 状态切换、冻结 |
| `POST /tasks/{id}/unfreeze` | 解冻，恢复冻结前状态 |
| `POST /tasks/{id}/archive` | 归档已完成任务 |
| `POST /tasks/{id}/steps` | 添加步骤：`{"title":"…"}` |
| `PUT /tasks/{id}/steps/{stepId}` | 修改步骤标题：`{"title":"…"}` |
| `DELETE /tasks/{id}/steps/{stepId}` | 删除步骤 |
| `PUT /tasks/{id}/steps/{stepId}/completion` | 设置完成状态：`{"completed":true}` |
| `GET /schedule/today` | 用户当地今天的日程 |
| `GET /schedule/rules` | 分页规则列表：page、pageSize |
| `GET /schedule/rules/{id}` | 规则详情 |
| `POST /schedule/rules` | 创建规则 |
| `PUT /schedule/rules/{id}` | 替换规则 |
| `DELETE /schedule/rules/{id}` | 删除规则，保留历史 |

任务列表参数：

| 参数 | 约定 |
|---|---|
| `page` | 从 1 开始，默认 1，最大 1000000 |
| `pageSize` | 1–100，默认 20 |
| `q` | 标题、描述和标签的包含搜索，最多 200 字符 |
| `tag` | 单个整标签精确匹配，ASCII 字母大小写不敏感 |
| `status` | 1 未开始、2 进行中、3 收敛中、4 已完成、5 冻结 |
| `priority` | 0–4 |
| `archived` | 默认 false；true 仅归档；空值 `archived=` 查询全部；MCP 使用 null 查询全部 |
| `sort` | updated（默认，更新时间倒序）、created（创建时间倒序）、priority（优先级倒序）、due（截止时间正序、无截止时间排最后）；以 ID 打破相同值排序 |

返回格式：

```json
{
  "items": [],
  "total": 0,
  "page": 1,
  "pageSize": 20,
  "timeZoneId": "Asia/Shanghai"
}
```

任务创建示例（PowerShell 7）：

```powershell
$headers = @{ Authorization = "Bearer $env:TATATASK_TOKEN" }
$body = @{
    title = "准备发布"
    priority = 3
    tags = "工作,发布"
    dueDate = "2026-10-20T18:00:00"
    steps = @("构建", "验收")
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "https://tasks.example.com/api/v1/tasks" `
    -Headers $headers -ContentType "application/json; charset=utf-8" -Body $body
```

更新任务使用完整的可编辑字段：`title`、`description`、`priority`、`tags`、`dueDate`、`dueWarningHours`。未提供的可空字段会清空；`priority` 默认变为 0。建议先读详情再修改提交。步骤可通过可选的 `stepsToAdd`、`stepIdsToDelete` 和 `stepsOrder` 修改，也可单独调用步骤接口。

规则创建与更新使用 `title`、`mode`（1 挑选、2 生成）、`daysOfWeek`（周一 1 至周日 64 的位掩码）、`startTime`、`endTime`（`HH:mm:ss`）、`tags`、`isActive`、`startDate`、`endDate`、`sortOrder`、`steps`（生成模式的步骤标题数组）。PUT 是替换语义。

## 时间、业务规则与重试

所有任务 DTO 的日期时间都是用户时区的墙钟时间，不附带 `Z` 或偏移量。例如 `2026-10-20T18:00:00`；用户时区可从 `/me` 或分页结果读取。传入带 Z 或偏移量的截止时间返回 400，避免意外二次换算。数据库继续存 UTC。

有未完成步骤不能进入已完成；冻结保存原状态，解冻恢复；归档只允许已完成任务。步骤完成使用显式布尔值，可安全重复提交。创建任务、添加步骤和创建规则没有幂等键，遇到不确定的网络结果时应先查询确认，避免盲目重试。

查询任务列表仍会执行自动归档，查询今日日程仍会执行惰性物化，因此「只读」表示禁止显式写入命令，查询的既有维护行为继续运行。任务列表排序是对外稳定字段排序，与看板的动态紧急度排序不同。

看板步骤完成与日程步骤推进是不同操作：`set_task_step_completion` 不推进日程条目。本版提供今日日程读取及规则 CRUD，尚未对外提供日程挑选、时段步骤推进、临时安排编辑和历史统计。

成功写入后，服务端向该用户已连接的网页发送 SignalR Refresh 通知。通知失败不会回滚已成功写入的数据。

REST 常见错误为 Problem Details JSON：400 参数错误、401 令牌无效/过期/撤销、403 无写权限、404 记录不存在（含不属于当前用户）、409 业务规则阻止操作、500 内部错误。

## 验证

```powershell
dotnet build
dotnet test
```

集成测试使用独立临时 SQLite 数据库，覆盖迁移、令牌权限与撤销、跨用户隔离、分页筛选、时区、状态门禁、日程物化、CORS、OpenAPI，以及官方 MCP 客户端的初始化、工具发现与调用。

在禁止测试宿主查询父进程的 Windows 沙箱中，可运行 `dotnet test -p:_MSTestEnableParentProcessQuery=false`。
