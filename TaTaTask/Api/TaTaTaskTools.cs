using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>
/// MCP 工具集：与 /api/v1 共用同一套服务与业务规则，认证同样使用访问令牌。
/// 工具名使用 snake_case，参数尽量平坦，方便各类 agent 直接调用。
/// </summary>
[McpServerToolType]
public sealed class TaTaTaskTools(
    TaskApiService tasks,
    ScheduleApiService schedule,
    UserApiService users)
{
    // ---------------- 任务 ----------------

    [McpServerTool(Name = "list_tasks", Destructive = false, OpenWorld = false)]
    [Description("分页查询当前用户的任务。支持按状态、标签、关键词、优先级、截止时间筛选与排序。返回 {items,page,pageSize,total,totalPages,hasNext}。")]
    public Task<CallToolResult> ListTasks(
        [Description("页码，从 1 开始")] int page = 1,
        [Description("每页条数，1-100，默认 20")] int pageSize = 20,
        [Description("状态：notStarted / inProgress / converging / done / frozen；不传表示全部")] string? status = null,
        [Description("完整标签精确匹配，例如 工作")] string? tag = null,
        [Description("标题/描述/标签的模糊搜索")] string? q = null,
        [Description("优先级 0-4")] int? priority = null,
        [Description("是否只看归档任务；默认 false")] bool? archived = false,
        [Description("截止时间下限，ISO 8601，例如 2026-10-01T00:00:00+08:00")] DateTimeOffset? dueFrom = null,
        [Description("截止时间上限，ISO 8601")] DateTimeOffset? dueTo = null,
        [Description("排序：updated（默认）、created、due、priority、sortOrder；前缀 - 表示倒序")] string? sort = null,
        CancellationToken cancellationToken = default)
        => Wrap(async () => await tasks.ListAsync(new TaskListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            Tag = tag,
            Q = q,
            Priority = priority,
            Archived = archived,
            DueFrom = dueFrom,
            DueTo = dueTo,
            Sort = sort,
        }, cancellationToken));

    [McpServerTool(Name = "get_task", ReadOnly = true)]
    [Description("按 Id 获取单个任务，包含全部子步骤。")]
    public Task<CallToolResult> GetTask([Description("任务 Id")] int id, CancellationToken cancellationToken = default)
        => Wrap(async () => await tasks.GetAsync(id));

    [McpServerTool(Name = "create_task", Destructive = false)]
    [Description("创建任务。时间参数使用 ISO 8601 并带时区偏移，例如 2026-10-20T18:00:00+08:00。")]
    public Task<CallToolResult> CreateTask(
        [Description("标题，1-200 字")] string title,
        [Description("描述，可选，最多 2000 字")] string? description = null,
        [Description("优先级 0-4，默认 0")] int priority = 0,
        [Description("标签数组，例如 [\"工作\",\"本周\"]")] string[]? tags = null,
        [Description("截止时间，ISO 8601 带偏移")] DateTimeOffset? dueAt = null,
        [Description("提前多少小时提醒")] int? dueWarningHours = null,
        [Description("初始状态，默认 notStarted；不能是 frozen 或带步骤的 done")] string? status = null,
        [Description("子步骤标题数组，按顺序创建")] string[]? steps = null,
        CancellationToken cancellationToken = default)
        => Wrap(async () => await tasks.CreateAsync(new CreateTaskRequest
        {
            Title = title,
            Description = description,
            Priority = priority,
            Tags = tags,
            DueDate = dueAt,
            DueWarningHours = dueWarningHours,
            Status = status,
            Steps = steps?.ToList(),
        }));

    [McpServerTool(Name = "update_task", Destructive = false)]
    [Description("局部更新任务：只修改传入的字段。tags 传空数组表示清空标签；clearDueDate=true 表示清空截止时间。")]
    public Task<CallToolResult> UpdateTask(
        [Description("任务 Id")] int id,
        [Description("新标题")] string? title = null,
        [Description("新描述；传空字符串表示清空")] string? description = null,
        [Description("优先级 0-4")] int? priority = null,
        [Description("新标签数组；空数组表示清空")] string[]? tags = null,
        [Description("新截止时间，ISO 8601 带偏移")] DateTimeOffset? dueAt = null,
        [Description("是否清空截止时间")] bool clearDueDate = false,
        [Description("提前提醒小时数")] int? dueWarningHours = null,
        [Description("要新增的子步骤标题")] string[]? stepsToAdd = null,
        [Description("要删除的子步骤 Id")] int[]? stepIdsToDelete = null,
        [Description("子步骤新顺序（步骤 Id 数组）")] int[]? stepsOrder = null,
        CancellationToken cancellationToken = default)
        => Wrap(async () => await tasks.PatchAsync(id, new PatchTaskRequest
        {
            Title = title,
            Description = description,
            Priority = priority,
            Tags = tags,
            DueDate = dueAt,
            ClearDueDate = clearDueDate,
            DueWarningHours = dueWarningHours,
            StepsToAdd = stepsToAdd?.ToList(),
            StepIdsToDelete = stepIdsToDelete?.ToList(),
            StepsOrder = stepsOrder?.ToList(),
        }));

    [McpServerTool(Name = "delete_task", Destructive = true)]
    [Description("删除任务。关联的日程项会保留标题快照并标记任务已删除。")]
    public Task<CallToolResult> DeleteTask([Description("任务 Id")] int id)
        => Wrap(async () =>
        {
            await tasks.DeleteAsync(id);
            return new { deleted = true, id };
        });

    [McpServerTool(Name = "set_task_status", Destructive = false)]
    [Description("变更任务状态：notStarted / inProgress / converging / done / frozen。解冻时传原状态即可。")]
    public Task<CallToolResult> SetTaskStatus(
        [Description("任务 Id")] int id,
        [Description("目标状态")] string status,
        [Description("冻结原因（进入 frozen 时使用）")] string? frozenReason = null,
        [Description("是否重置所有子步骤")] bool resetSteps = false)
        => Wrap(async () => await tasks.ChangeStatusAsync(id, new ChangeTaskStatusRequest
        {
            Status = status,
            FrozenReason = frozenReason,
            ResetSteps = resetSteps,
        }));

    [McpServerTool(Name = "archive_task", Destructive = false)]
    [Description("归档任务（只有已完成任务可以归档）。")]
    public Task<CallToolResult> ArchiveTask([Description("任务 Id")] int id)
        => Wrap(async () => await tasks.ArchiveAsync(id));

    [McpServerTool(Name = "unarchive_task", Destructive = false)]
    [Description("取消归档。")]
    public Task<CallToolResult> UnarchiveTask([Description("任务 Id")] int id)
        => Wrap(async () => await tasks.UnarchiveAsync(id));

    [McpServerTool(Name = "add_task_step", Destructive = false)]
    [Description("给任务新增一个子步骤。")]
    public Task<CallToolResult> AddTaskStep(
        [Description("任务 Id")] int taskId,
        [Description("子步骤标题")] string title)
        => Wrap(async () => await tasks.AddStepAsync(taskId, title));

    [McpServerTool(Name = "update_task_step", Destructive = false)]
    [Description("重命名子步骤。")]
    public Task<CallToolResult> UpdateTaskStep(
        [Description("任务 Id")] int taskId,
        [Description("子步骤 Id")] int stepId,
        [Description("新标题")] string title)
        => Wrap(async () => await tasks.UpdateStepAsync(taskId, stepId, title));

    [McpServerTool(Name = "delete_task_step", Destructive = true)]
    [Description("删除子步骤。")]
    public Task<CallToolResult> DeleteTaskStep(
        [Description("任务 Id")] int taskId,
        [Description("子步骤 Id")] int stepId)
        => Wrap(async () => await tasks.DeleteStepAsync(taskId, stepId));

    [McpServerTool(Name = "toggle_task_step", Destructive = false)]
    [Description("切换子步骤完成状态。")]
    public Task<CallToolResult> ToggleTaskStep(
        [Description("任务 Id")] int taskId,
        [Description("子步骤 Id")] int stepId)
        => Wrap(async () => await tasks.ToggleStepAsync(taskId, stepId));

    [McpServerTool(Name = "set_task_step_completion", Destructive = false, Idempotent = true)]
    [Description("显式设置子步骤完成状态（幂等，适合重试）。")]
    public Task<CallToolResult> SetTaskStepCompletion(
        [Description("任务 Id")] int taskId,
        [Description("子步骤 Id")] int stepId,
        [Description("是否完成")] bool completed)
        => Wrap(async () => await tasks.SetStepCompletionAsync(taskId, stepId, completed));

    [McpServerTool(Name = "list_tags", ReadOnly = true)]
    [Description("列出当前用户的标签及任务数量（按使用次数倒序）。")]
    public Task<CallToolResult> ListTags([Description("是否统计已归档任务")] bool includeArchived = false)
        => Wrap(async () => await users.GetTagsAsync(includeArchived));

    [McpServerTool(Name = "get_stats", Destructive = false, OpenWorld = false)]
    [Description("获取看板与日程的汇总统计：活跃/逾期数量、周吞吐、标签分布、日程完成率等。")]
    public Task<CallToolResult> GetStats() => Wrap(async () => await users.GetStatsAsync());

    [McpServerTool(Name = "get_me", ReadOnly = true)]
    [Description("获取当前令牌对应的用户信息、时区与令牌权限。")]
    public Task<CallToolResult> GetMe() => Wrap(async () => await users.GetMeAsync());

    // ---------------- 日程 ----------------

    [McpServerTool(Name = "get_today_schedule", Destructive = false, OpenWorld = false)]
    [Description("获取今天的日程（会惰性补齐当天规则条目），包含当前时段、待选与未完成项。")]
    public Task<CallToolResult> GetTodaySchedule() => Wrap(async () => await schedule.GetTodayAsync());

    [McpServerTool(Name = "list_schedule_rules", ReadOnly = true)]
    [Description("分页列出周规则（星期、时段、模式、标签、步骤模板）。")]
    public Task<CallToolResult> ListScheduleRules(
        [Description("页码，从 1 开始")] int page = 1,
        [Description("每页条数，1-100")] int pageSize = 20)
        => Wrap(async () => await schedule.ListRulesAsync(page, pageSize));

    [McpServerTool(Name = "get_schedule_rule", ReadOnly = true)]
    [Description("按 Id 获取一条周规则。")]
    public Task<CallToolResult> GetScheduleRule([Description("规则 Id")] int id)
        => Wrap(async () => await schedule.GetRuleAsync(id));

    [McpServerTool(Name = "create_schedule_rule", Destructive = false)]
    [Description("创建周规则。days 使用 mon/tue/wed/thu/fri/sat/sun；mode 为 pick 或 generate；时间格式 HH:mm。")]
    public Task<CallToolResult> CreateScheduleRule(
        [Description("规则标题")] string title,
        [Description("模式：pick（从看板挑任务）或 generate（每天自动建卡）")] string mode = "pick",
        [Description("星期数组，例如 [\"mon\",\"wed\",\"fri\"]")] string[]? days = null,
        [Description("星期位掩码（与 days 二选一，days 优先）")] int? daysOfWeek = null,
        [Description("开始时间 HH:mm")] string? startTime = null,
        [Description("结束时间 HH:mm")] string? endTime = null,
        [Description("标签数组")] string[]? tags = null,
        [Description("是否启用")] bool isActive = true,
        [Description("生效起始日期 yyyy-MM-dd")] string? startDate = null,
        [Description("生效结束日期 yyyy-MM-dd")] string? endDate = null,
        [Description("排序值")] int sortOrder = 0,
        [Description("生成模式的步骤模板")] string[]? steps = null)
        => Wrap(async () => await schedule.CreateRuleAsync(BuildRuleRequest(
            title, mode, days, daysOfWeek, startTime, endTime, tags, isActive, startDate, endDate, sortOrder, steps)));

    [McpServerTool(Name = "update_schedule_rule", Destructive = false)]
    [Description("更新周规则；未传的字段沿用原值。")]
    public Task<CallToolResult> UpdateScheduleRule(
        [Description("规则 Id")] int id,
        [Description("规则标题")] string? title = null,
        [Description("模式：pick 或 generate")] string? mode = null,
        [Description("星期数组，例如 [\"mon\",\"fri\"]")] string[]? days = null,
        [Description("星期位掩码")] int? daysOfWeek = null,
        [Description("开始时间 HH:mm")] string? startTime = null,
        [Description("结束时间 HH:mm")] string? endTime = null,
        [Description("标签数组")] string[]? tags = null,
        [Description("是否启用")] bool? isActive = null,
        [Description("生效起始日期 yyyy-MM-dd")] string? startDate = null,
        [Description("生效结束日期 yyyy-MM-dd")] string? endDate = null,
        [Description("排序值")] int? sortOrder = null,
        [Description("步骤模板")] string[]? steps = null,
        [Description("清除生效起始日期")] bool clearStartDate = false,
        [Description("清除生效结束日期")] bool clearEndDate = false)
        => Wrap(async () => await schedule.PatchRuleAsync(id, new PatchScheduleRuleApiRequest
        {
            Title = title, Mode = mode, Days = days, DaysOfWeek = daysOfWeek,
            StartTime = startTime, EndTime = endTime, Tags = tags, IsActive = isActive,
            StartDate = ParseDate(startDate), EndDate = ParseDate(endDate),
            ClearStartDate = clearStartDate, ClearEndDate = clearEndDate,
            SortOrder = sortOrder, Steps = steps?.ToList(),
        }));

    [McpServerTool(Name = "delete_schedule_rule", Destructive = true)]
    [Description("删除周规则；历史日程项与已生成的任务会保留。")]
    public Task<CallToolResult> DeleteScheduleRule([Description("规则 Id")] int id)
        => Wrap(async () =>
        {
            await schedule.DeleteRuleAsync(id);
            return new { deleted = true, id };
        });

    [McpServerTool(Name = "list_schedule_history", ReadOnly = true)]
    [Description("按日期区间查询日程历史（按天分页）。默认最近 7 天。")]
    public Task<CallToolResult> ListScheduleHistory(
        [Description("起始日期 yyyy-MM-dd")] string? from = null,
        [Description("结束日期 yyyy-MM-dd")] string? to = null,
        [Description("只返回该规则相关的条目")] int? ruleId = null,
        [Description("页码，从 1 开始")] int page = 1,
        [Description("每页天数，1-100")] int pageSize = 20)
        => Wrap(async () => await schedule.ListHistoryAsync(
            ParseDate(from), ParseDate(to), ruleId, page, pageSize));

    [McpServerTool(Name = "get_schedule_candidates", ReadOnly = true)]
    [Description("获取某条日程项的候选任务（挑选模式）。")]
    public Task<CallToolResult> GetScheduleCandidates([Description("日程项 Id")] int entryId)
        => Wrap(async () => await schedule.GetCandidatesAsync(entryId));

    [McpServerTool(Name = "pick_schedule_task", Destructive = false)]
    [Description("把候选任务排进某条日程项；stepIds 为空表示今天做全部子步骤。")]
    public Task<CallToolResult> PickScheduleTask(
        [Description("日程项 Id")] int entryId,
        [Description("任务 Id")] int todoItemId,
        [Description("今天要做的子步骤 Id 数组")] int[]? stepIds = null)
        => Wrap(async () => await schedule.PickAsync(entryId, new PickTaskApiRequest
        {
            TodoItemId = todoItemId,
            StepIds = stepIds?.ToList(),
        }));

    [McpServerTool(Name = "set_schedule_entry_steps", Destructive = false)]
    [Description("改今天排定的子步骤。")]
    public Task<CallToolResult> SetScheduleEntrySteps(
        [Description("日程项 Id")] int entryId,
        [Description("今天的子步骤 Id 数组")] int[] stepIds)
        => Wrap(async () => await schedule.SetEntryStepsAsync(entryId, new SetEntryStepsApiRequest
        {
            StepIds = stepIds.ToList(),
        }));

    [McpServerTool(Name = "toggle_schedule_step", Destructive = false)]
    [Description("勾/取消一个今日步骤，并推进「日程项完成 → 任务完成」链条。")]
    public Task<CallToolResult> ToggleScheduleStep(
        [Description("日程项 Id")] int entryId,
        [Description("子步骤 Id")] int stepId)
        => Wrap(async () => await schedule.ToggleEntryStepAsync(entryId, stepId));

    [McpServerTool(Name = "complete_schedule_entry", Destructive = false)]
    [Description("无子步骤的日程项直接完成。")]
    public Task<CallToolResult> CompleteScheduleEntry([Description("日程项 Id")] int entryId)
        => Wrap(async () => await schedule.CompleteEntryAsync(entryId));

    [McpServerTool(Name = "create_manual_entry", Destructive = false)]
    [Description("手动加一条临时日程项。填标题则自动建卡并排上，不填则留作待选。")]
    public Task<CallToolResult> CreateManualEntry(
        [Description("开始时间 HH:mm，默认 09:00")] string? startTime = null,
        [Description("结束时间 HH:mm，默认 10:00")] string? endTime = null,
        [Description("标签数组")] string[]? tags = null,
        [Description("事项标题；填写则自动建卡")] string? title = null)
        => Wrap(async () => await schedule.CreateManualEntryAsync(new ManualEntryApiRequest
        {
            StartTime = startTime,
            EndTime = endTime,
            Tags = tags,
            Title = title,
        }));

    [McpServerTool(Name = "skip_rule_today", Destructive = false)]
    [Description("跳过某条规则在今天（不产生任务）。")]
    public Task<CallToolResult> SkipRuleToday([Description("规则 Id")] int ruleId)
        => Wrap(async () => await schedule.SkipTodayAsync(ruleId));

    [McpServerTool(Name = "carry_over_entry", Destructive = false)]
    [Description("把昨日未完成的日程项排进今天。")]
    public Task<CallToolResult> CarryOverEntry([Description("日程项 Id")] int entryId)
        => Wrap(async () => await schedule.CarryOverAsync(entryId));

    [McpServerTool(Name = "freeze_entry_task", Destructive = false)]
    [Description("冻结昨日未完成项关联的任务。")]
    public Task<CallToolResult> FreezeEntryTask([Description("日程项 Id")] int entryId)
        => Wrap(async () => await schedule.FreezeEntryTaskAsync(entryId));

    [McpServerTool(Name = "unfreeze_task", Destructive = false)]
    [Description("解冻任务并恢复冻结前的状态。")]
    public Task<CallToolResult> UnfreezeTask([Description("任务 Id")] int id)
        => Wrap(async () => await tasks.UnfreezeAsync(id));

    [McpServerTool(Name = "reorder_task_steps", Destructive = false, Idempotent = true)]
    [Description("重排任务子步骤，按步骤 Id 数组的顺序排列。")]
    public Task<CallToolResult> ReorderTaskSteps(int taskId, int[] stepIds)
        => Wrap(async () =>
        {
            await tasks.ReorderStepsAsync(taskId, stepIds.ToList());
            return new { reordered = true, taskId };
        });

    [McpServerTool(Name = "update_manual_entry", Destructive = false)]
    [Description("局部更新今天的临时安排，未传的字段保持原值。")]
    public Task<CallToolResult> UpdateManualEntry(int entryId, string? startTime = null,
        string? endTime = null, string[]? tags = null, string? title = null)
        => Wrap(async () => await schedule.UpdateManualEntryAsync(entryId, new ManualEntryApiRequest
        {
            StartTime = startTime, EndTime = endTime, Tags = tags, Title = title,
        }));

    [McpServerTool(Name = "delete_manual_entry", Destructive = true)]
    [Description("删除今天的临时安排：自动建的任务一起删除；挑入的看板任务保留。")]
    public Task<CallToolResult> DeleteManualEntry(int entryId)
        => Wrap(async () => await schedule.DeleteManualEntryAsync(entryId));

    [McpServerTool(Name = "delete_entry_task", Destructive = true)]
    [Description("删除未完成日程项关联的任务，保留日程快照。")]
    public Task<CallToolResult> DeleteEntryTask(int entryId)
        => Wrap(async () => await schedule.DeleteEntryTaskAsync(entryId));

    // ---------------- 辅助 ----------------

    private static SaveScheduleRuleApiRequest BuildRuleRequest(
        string title, string mode, string[]? days, int? daysOfWeek,
        string? startTime, string? endTime, string[]? tags, bool isActive,
        string? startDate, string? endDate, int sortOrder, string[]? steps) => new()
        {
            Title = title,
            Mode = mode,
            Days = days,
            DaysOfWeek = daysOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            Tags = tags,
            IsActive = isActive,
            StartDate = ParseDate(startDate),
            EndDate = ParseDate(endDate),
            SortOrder = sortOrder,
            Steps = steps?.ToList(),
        };

    private static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : throw ApiException.BadRequest($"无效的日期：{text}，应为 yyyy-MM-dd");
    }

    /// <summary>把业务异常转成结构化的工具错误，避免以未处理异常的形式返回。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<CallToolResult> Wrap<T>(Func<Task<T>> action)
    {
        try
        {
            var data = JsonSerializer.SerializeToElement(await action(), JsonOptions);
            return new() { StructuredContent = data, Content = [new TextContentBlock { Text = data.GetRawText() }] };
        }
        catch (Exception ex) when (ex is ApiException or KeyNotFoundException or InvalidOperationException
            or ArgumentException or UnauthorizedAccessException)
        {
            var status = ex switch
            {
                ApiException api => api.StatusCode,
                KeyNotFoundException => 404,
                UnauthorizedAccessException => 401,
                ArgumentException => 400,
                _ => 409,
            };
            var data = JsonSerializer.SerializeToElement(new { error = true, status, message = ex.Message }, JsonOptions);
            return new() { IsError = true, StructuredContent = data, Content = [new TextContentBlock { Text = data.GetRawText() }] };
        }
    }
}
