# 外部接入：REST API 与 MCP

TaTaTask 在同一进程内提供两个对外入口，供**其他网页**和**AI agent**（Codex、dsh、Claude Code、Cursor 等）读写你自己的数据：

| 入口 | 地址 | 用途 |
|---|---|---|
| REST API | `/api/v1` | 网页后端、脚本、任何 HTTP 客户端 |
| MCP | `/mcp` | 支持 Streamable HTTP 的 MCP 客户端（agent） |
| OpenAPI | `/openapi/v1.json` | 机器可读的接口文档 |

两个入口共用同一套服务层、业务规则和访问令牌，只能访问令牌所属用户的数据。

---

## 1. 创建访问令牌

令牌是 Bearer 令牌，形如 `ttt_xxxxxxxx…`，**明文只在创建后显示一次**；数据库只保存 SHA-256 摘要和展示前缀。

### 方式 A：网页

登录后打开「设置」页 →「外部接入令牌」，或直接打开 `/integrations`：

1. 填写名称（如 `Codex`、`家庭网页`）；
2. 需要写操作时打开「允许写入」，否则是只读令牌；
3. 设置有效天数（0 = 永不过期）；
4. 创建后立即复制令牌。

同一页面可查看最后使用时间并随时吊销。每个账号最多保留 50 个有效令牌。

### 方式 B：命令行（无浏览器 / 首次部署）

```bash
# 在部署目录运行发布的可执行文件；应用日志写 stderr，创建时 stdout 只有一次令牌明文
cd /opt/tatatask
./TaTaTask --create-api-token alice --token-name codex --token-scope write --token-days 365

# 列出某用户的令牌（不含明文）
./TaTaTask --list-api-tokens alice
```

### 使用

```http
Authorization: Bearer ttt_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

- REST 只读令牌（默认）只能查询；写操作返回 `403`。MCP 写工具返回 `isError: true` 及结构化状态码。
- 网页及旧 `/api/*` 只接受 Cookie；Bearer 令牌只用于 `/api/v1`、`/mcp` 和 OpenAPI，不能借旧接口绕过 scope。
- CLI 的 `--token-days` 范围为 0–365，省略或 0 表示永不过期，非法值以非零退出码报错。
- 吊销或过期后，后续请求立即失效。
- 令牌等同于个人凭据：不要写进公开网页的前端 JS、Git 仓库或截图；放在服务端环境变量里。

---

## 2. 通用约定

### 时间格式

外部接口的时间统一为**带时区偏移的 ISO 8601**，例如：

```
2026-10-20T18:30:00+08:00
```

偏移量取自用户的时区设置（`GET /api/v1/me` 可查 `timeZoneId`）。请求里也可以传 UTC 时间（`...Z`）或其他偏移，服务端按同一时刻处理。

- 日期字段（规则生效区间、历史查询）：`yyyy-MM-dd`
- 时刻字段（日程时段、`nowTime`）：`HH:mm`，有秒时为 `HH:mm:ss`；非法格式返回 400，不会退回默认时段。

### 分页

列表接口统一返回：

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "total": 0,
  "totalPages": 0,
  "hasNext": false,
  "hasPrevious": false
}
```

查询参数：`page`（从 1 开始）、`pageSize`（默认 20，范围 1–100；页码范围 1–1,000,000，非法值返回 400）。

### 错误

失败时返回 RFC 7807 `ProblemDetails`：

```json
{ "status": 409, "title": "所有子步骤完成后才能进入已完成", "instance": "/api/v1/tasks/12/status" }
```

| 状态码 | 含义 |
|---|---|
| 400 | 参数不合法 |
| 401 | 缺少或无效的令牌 |
| 403 | 令牌只读，或没有该操作权限 |
| 404 | 记录不存在或不属于当前用户 |
| 409 | 与当前状态冲突（如未完成子步骤就完成任务、归档未完成任务） |
| 500 | 服务端错误 |

### 浏览器跨域（CORS）

默认只允许同源。需要「其他网页」直接从浏览器跨域调用时，在 `appsettings.json` 配置**精确来源**（协议 + 域名 + 端口，无路径）：

```json
{
  "ExternalAccess": {
    "AllowedOrigins": ["https://portal.example.com", "http://localhost:5173"]
  }
}
```

环境变量形式：`ExternalAccess__AllowedOrigins__0=https://portal.example.com`。修改后需重启。

> 更推荐让网页的后端转发请求：令牌只保存在后端，不暴露给浏览器。

---

## 3. REST API

Base URL：`https://<你的域名>/api/v1`。所有请求都需要 `Authorization: Bearer <token>`。

### 当前用户

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/me` | 用户 Id、用户名、时区、默认提醒值、令牌名与读写权限 |

### 任务

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/tasks` | 分页查询，见下方筛选参数 |
| GET | `/tasks/{id}` | 单个任务（含子步骤） |
| POST | `/tasks` | 创建任务 |
| PATCH | `/tasks/{id}` | 局部更新，只改传入字段 |
| DELETE | `/tasks/{id}` | 删除任务（日程项保留快照） |
| POST | `/tasks/{id}/status` | 状态流转 / 冻结 / 解冻 |
| POST | `/tasks/{id}/archive` | 归档（仅已完成任务） |
| POST | `/tasks/{id}/unfreeze` | 恢复冻结前的状态 |
| POST | `/tasks/{id}/unarchive` | 取消归档 |
| POST | `/tasks/{id}/steps` | 新增子步骤 |
| PATCH | `/tasks/{id}/steps/{stepId}` | 重命名子步骤 |
| DELETE | `/tasks/{id}/steps/{stepId}` | 删除子步骤 |
| POST | `/tasks/{id}/steps/{stepId}/toggle` | 切换子步骤完成状态 |
| PUT | `/tasks/{id}/steps/{stepId}/completion` | 显式设置完成状态（幂等） |
| PUT | `/tasks/{id}/steps/order` | 重排子步骤 |

`GET /tasks` 查询参数：

| 参数 | 说明 |
|---|---|
| `status` | `notStarted` / `inProgress` / `converging` / `done` / `frozen` |
| `tag` | 完整标签精确匹配 |
| `q` | 标题、描述、标签模糊搜索 |
| `priority` | 0–4 |
| `archived` | `true` / `false`，默认 `false`；传空值表示全部 |
| `dueFrom` / `dueTo` | 截止时间范围（ISO 8601） |
| `sort` | `-updated`（默认）、`created`、`due`、`priority`、`sortOrder`；前缀 `-` 表示倒序 |

`PATCH /tasks/{id}` 的字段语义：未出现的字段不变；`tags: []` 清空标签；`clearDueDate: true` 清空截止时间；`description: ""` 清空描述。子步骤可通过 `stepsToAdd`、`stepIdsToDelete`、`stepsOrder` 一次更新。

任务状态：

```json
{
  "id": 42,
  "title": "写周报",
  "priority": 2,
  "tags": ["工作"],
  "dueDate": "2026-10-20T18:30:00+08:00",
  "status": "inProgress",
  "steps": [
    { "id": 7, "title": "整理数据", "isCompleted": true, "sortOrder": 1 },
    { "id": 8, "title": "成稿", "isCompleted": false, "sortOrder": 2 }
  ],
  "totalSteps": 2,
  "doneSteps": 1,
  "currentStepId": 8,
  "isArchived": false,
  "createdAt": "2026-10-18T09:00:00+08:00",
  "updatedAt": "2026-10-19T10:20:00+08:00"
}
```

创建任务：

```bash
curl -X POST https://tasks.example.com/api/v1/tasks \
  -H "Authorization: Bearer $TATATASK_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "写周报",
    "description": "本周进展",
    "priority": 2,
    "tags": ["工作", "本周"],
    "dueDate": "2026-10-20T18:30:00+08:00",
    "dueWarningHours": 8,
    "steps": ["整理数据", "成稿"]
  }'
```

列表 + 分页：

```bash
curl "https://tasks.example.com/api/v1/tasks?status=inProgress&tag=工作&page=1&pageSize=20" \
  -H "Authorization: Bearer $TATATASK_TOKEN"
```

完成任务前必须先完成所有子步骤，否则 `409`；这是应用的步骤门禁，外部接口不能绕过。

### 标签与统计

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/tags?includeArchived=false` | 标签及任务数，按使用次数倒序 |
| GET | `/stats` | 活跃/逾期数量、周吞吐、标签分布、日程完成率 |

### 日程

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/schedule/today` | 今日日程（惰性补齐当天规则条目） |
| GET | `/schedule/rules` | 分页列出周规则 |
| GET | `/schedule/rules/{id}` | 单条规则 |
| POST | `/schedule/rules` | 创建规则 |
| PATCH | `/schedule/rules/{id}` | 局部更新规则，未传字段保持原值 |
| PUT | `/schedule/rules/{id}` | 完整替换规则 |
| DELETE | `/schedule/rules/{id}` | 删除规则（历史条目保留） |
| POST | `/schedule/rules/{id}/skip-today` | 跳过今天 |
| GET | `/schedule/history` | 历史，按天分页；参数 `from`、`to`（默认最近 7 天）、`ruleId` |
| GET | `/schedule/entries/{id}/candidates` | 挑选模式的候选任务 |
| POST | `/schedule/entries` | 新增临时日程项 |
| PATCH | `/schedule/entries/{id}` | 局部编辑临时安排，保留未传时段、标签及标题 |
| DELETE | `/schedule/entries/{id}` | 删除临时安排 |
| POST | `/schedule/entries/{id}/pick` | 把候选任务排进时段 |
| PUT | `/schedule/entries/{id}/steps` | 改今天要做的子步骤 |
| POST | `/schedule/entries/{id}/steps/{stepId}/toggle` | 勾/取消今日步骤，推进完成链条 |
| POST | `/schedule/entries/{id}/complete` | 无子步骤的条目直接完成 |
| POST | `/schedule/entries/{id}/carry-over` | 昨日未完成项排进今天 |
| POST | `/schedule/entries/{id}/freeze` | 冻结关联任务 |
| DELETE | `/schedule/entries/{id}/task` | 删除关联任务 |

创建规则示例（`days` 与 `daysOfWeek` 二选一，`days` 优先）：

```bash
curl -X POST https://tasks.example.com/api/v1/schedule/rules \
  -H "Authorization: Bearer $TATATASK_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "晨间写作",
    "mode": "pick",
    "days": ["mon", "wed", "fri"],
    "startTime": "07:30",
    "endTime": "08:30",
    "tags": ["写作"]
  }'
```

规则 PATCH 可用 `clearStartDate` / `clearEndDate` 清除生效日期。历史单次查询最多 366 天。

> `mode: "pick"` 从看板按标签挑任务；`mode: "generate"` 每天自动建卡，`steps` 为步骤模板。

### OpenAPI

`GET /openapi/v1.json` 返回 OpenAPI 3 文档（同样需要令牌）：

```bash
curl -H "Authorization: Bearer $TATATASK_TOKEN" https://tasks.example.com/openapi/v1.json
```

---

## 4. MCP

端点：`https://<你的域名>/mcp`，使用 **Streamable HTTP**、无状态，每次请求都由 `Authorization` 头校验令牌。只读令牌可以调用查询工具；写工具会返回错误。

### 客户端配置

Codex（`~/.codex/config.toml`）：

```toml
[mcp_servers.tatatask]
url = "https://tasks.example.com/mcp"
bearer_token_env_var = "TATATASK_TOKEN"
```

Claude Code：

```bash
export TATATASK_TOKEN=ttt_...
claude mcp add --transport http tatatask https://tasks.example.com/mcp \
  --header "Authorization: Bearer $TATATASK_TOKEN"
```

Cursor / VS Code 等使用 JSON 配置的客户端：

```json
{
  "mcpServers": {
    "tatatask": {
      "url": "https://tasks.example.com/mcp",
      "headers": { "Authorization": "Bearer ttt_xxxxxxxx" }
    }
  }
}
```

dsh 等支持远程 MCP 的客户端同理：填上面的 URL，并在请求头里带 `Authorization`。

只支持 stdio 的客户端可以用代理桥接：

```bash
npx -y mcp-remote https://tasks.example.com/mcp --header "Authorization: Bearer $TATATASK_TOKEN"
```

### 工具列表

| 工具 | 说明 |
|---|---|
| `list_tasks` | 分页查询任务（状态/标签/关键词/优先级/截止时间/排序） |
| `get_task` | 取单个任务 |
| `create_task` | 创建任务（标题、描述、优先级、标签、截止时间、步骤） |
| `update_task` | 局部更新任务 |
| `delete_task` | 删除任务 |
| `set_task_status` | 状态流转 / 冻结 / 解冻 |
| `unfreeze_task` | 恢复冻结前状态 |
| `reorder_task_steps` | 重排子步骤 |
| `archive_task` / `unarchive_task` | 归档 / 取消归档 |
| `add_task_step` / `update_task_step` / `delete_task_step` | 子步骤增删改 |
| `toggle_task_step` / `set_task_step_completion` | 勾选 / 显式设置完成 |
| `list_tags` | 标签及计数 |
| `get_stats` | 看板与日程统计 |
| `get_me` | 当前用户、时区、令牌权限 |
| `get_today_schedule` | 今日日程 |
| `list_schedule_rules` / `get_schedule_rule` | 周规则 |
| `create_schedule_rule` / `update_schedule_rule` / `delete_schedule_rule` | 规则增删改 |
| `list_schedule_history` | 日程历史 |
| `get_schedule_candidates` | 候选任务 |
| `pick_schedule_task` / `set_schedule_entry_steps` | 排任务 / 改今日步骤 |
| `toggle_schedule_step` / `complete_schedule_entry` | 推进今日日程 |
| `create_manual_entry` / `update_manual_entry` / `delete_manual_entry` | 临时安排增改删 |
| `delete_entry_task` | 删除日程关联任务 |
| `skip_rule_today` | 跳过今天 |
| `carry_over_entry` / `freeze_entry_task` | 昨日未完成处置 |

共 37 个工具。业务、参数、权限错误同时设置 `isError: true`，并返回 `structuredContent: {error,status,message}`；成功也返回结构化 JSON。所有外部写命令在同一个事务中执行，提交后通知同账号网页；通知失败不撤销已提交结果。

工具结果中的时间同样是带偏移的 ISO 8601；数值枚举用可读字符串（如 `notStarted`、`inProgress`）。

---

## 5. 部署与安全

- 公网部署请使用 HTTPS；家庭 IPv6 + Caddy + EdgeOne 的配置和双栈验收见 [部署说明](caddy-edgeone.md)。
- 启动时自动迁移数据库并创建 `ApiTokens` 表，无需手工执行 SQL。
- 令牌按用户隔离：接口不接受 `userId` 参数，数据范围完全由令牌决定。
- 建议每个客户端使用独立令牌，便于单独吊销；定期用 `/integrations`（令牌管理）检查最后使用时间。
- 当前版本未内置限流；如需限制外部调用频率，请在反向代理层配置。

## 6. 整合版本兼容性

保留原 `AddApiTokens` 迁移，以增量迁移加入最后使用时间和可选过期时间；原分支已签发的 `ttt_` 令牌仍可使用。也接受 `tt_` 前缀。API 契约统一采用本页的标签数组、字符串状态与带偏移时间；此前实验版的墙钟时间、数字状态及 `/openapi/external.json` 地址需按本页调整。
