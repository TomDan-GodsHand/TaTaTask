# TaTaTask

自托管的看板式 TODO 管理 Web 应用，面向家庭/小团队（2-5人）。

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
- **看板**：5 个预设泳道（未开始 / 进行中 / 收敛中 / 已完成 / 冻结），横向滚动
- **任务**：标题、优先级（! / !! / !!!）、标签（徽章）、截止日期（过期/今天/未来 着色）、拖拽移动
- **子步骤**：任务内多步骤，展开折叠、勾选完成、独立排序
- **归档**：已完成任务一键归档，支持标签/日期/标题筛选，可恢复

## 页面

| 路由        | 页面     | 渲染模式          | 认证   |
|-------------|----------|-------------------|--------|
| `/`         | 首页     | InteractiveServer | 否     |
| `/login`    | 登录     | SSR               | 否     |
| `/register` | 注册     | SSR               | 否     |
| `/todos`    | 看板     | InteractiveAuto   | 是     |
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

- [ ] 看板数据模型与数据库迁移
- [ ] `/todos` 看板页：5 泳道横向滚动布局
- [ ] 新建任务（未开始泳道底部输入）
- [ ] 任务卡片展示（标题、优先级、标签徽章、截止日期着色）
- [ ] 行内编辑标题
- [ ] 删除任务（确认后硬删除）
- [ ] 拖拽泳道间移动（自动变更状态 + 插入顶部）
- [ ] 泳道内排序（优先级降序 → SortOrder 升序）

### Phase 4 — 子步骤

- [ ] 子步骤数据模型与迁移
- [ ] 展开/折叠步骤列表
- [ ] 添加子步骤
- [ ] Checkbox 切换完成状态
- [ ] 删除子步骤
- [ ] 子步骤排序

### Phase 5 — 归档系统

- [ ] 归档操作（仅"已完成"泳道任务）
- [ ] `/archive` 归档浏览页（按归档时间倒序）
- [ ] 筛选：标签模糊匹配、日期范围、标题关键词搜索
- [ ] 恢复：取消归档，任务回到看板

### Phase 6 — 交付打磨

- [ ] 错误处理与异常页面
- [ ] 前端表单验证
- [ ] 响应式适配
- [ ] `linux-x64` 发布与部署文档

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
