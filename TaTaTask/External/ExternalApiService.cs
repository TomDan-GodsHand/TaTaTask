using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaTaTask.Client.Services;
using TaTaTask.Data;
using TaTaTask.Hubs;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Enums;
using TaTaTask.Services;

namespace TaTaTask.External;

public sealed class ExternalApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

// Both transports use this boundary for validation, permissions, transactions and change notifications.
public sealed class ExternalApiService(
    ServerTodoService todos, IScheduleService schedule, AppDbContext db,
    IHttpContextAccessor accessor, ICurrentUser current, IHubContext<TodoHub> hub,
    ILogger<ExternalApiService> logger)
{
    private void Require(bool write = false)
    {
        var principal = accessor.HttpContext?.User;
        if (current.UserId is null || principal?.Identity?.AuthenticationType != ExternalAuth.Scheme)
            throw new ExternalApiException(401, "需要有效的访问令牌");
        if (!principal.HasClaim(ExternalAuth.Scope, write ? "write" : "read"))
            throw new ExternalApiException(403, "访问令牌没有写入权限");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new ExternalApiException(400, message);
    }

    private static T Found<T>(T? value) where T : class
        => value ?? throw new ExternalApiException(404, "记录不存在");

    public async Task<object> MeAsync()
    {
        Require();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == current.UserId);
        return new { user.Username, user.TimeZoneId, CanWrite = accessor.HttpContext!.User.HasClaim(ExternalAuth.Scope, "write") };
    }

    public Task<PagedResult<TodoItemDto>> ListAsync(TaskQuery query, CancellationToken ct = default)
    {
        Require();
        var errors = new List<ValidationResult>();
        Check(Validator.TryValidateObject(query, new ValidationContext(query), errors, true),
            string.Join("; ", errors.Select(e => e.ErrorMessage)));
        Check(query.Status is null || Enum.IsDefined(query.Status.Value), "无效的任务状态");
        Check(query.Sort is "updated" or "created" or "priority" or "due", "排序需为 updated、created、priority 或 due");
        Check(query.Tag is null || !query.Tag.Contains(',') && !query.Tag.Contains('，'), "tag 只接受一个完整标签");
        return todos.QueryAsync(query, ct);
    }

    public async Task<TodoItemDto> GetAsync(int id)
    {
        Require();
        return Found(await todos.GetByIdAsync(id));
    }

    private async Task<T> WriteAsync<T>(Func<Task<T>> action)
    {
        Require(write: true);
        await using var transaction = await db.Database.BeginTransactionAsync();
        T result;
        try
        {
            result = await action();
            await transaction.CommitAsync();
        }
        catch (KeyNotFoundException) { throw new ExternalApiException(404, "记录不存在"); }
        catch (InvalidOperationException ex) { throw new ExternalApiException(409, ex.Message); }

        // The command already committed: notification failures must not make clients retry the write.
        try { await hub.Clients.Group($"user_{current.UserId}").SendAsync("Refresh"); }
        catch (Exception ex) { logger.LogWarning(ex, "External change committed but refresh notification failed"); }
        return result;
    }

    private async Task ValidateTaskAsync(string? title, string? description, string? tags, int priority,
        DateTime? dueDate, int? warningHours, IEnumerable<string>? steps)
    {
        Check(!string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 200, "标题需为 1–200 个字符");
        Check(description is null || description.Length <= 2000, "描述最多 2000 个字符");
        Check(tags is null || tags.Length <= 500, "标签最多 500 个字符");
        Check(priority is >= 0 and <= 4, "优先级需为 0–4");
        Check(warningHours is null or >= 0, "提醒小时数不能为负数");
        Check(steps is null || steps.Count() <= 100 && steps.All(ValidTitle), "步骤最多 100 个，标题需为 1–200 个字符");
        if (dueDate.HasValue)
        {
            Check(dueDate.Value.Kind == DateTimeKind.Unspecified, "时间需为用户时区下的墙钟时间，不要附带 Z 或偏移量");
            var tzId = await db.Users.Where(u => u.Id == current.UserId).Select(u => u.TimeZoneId).SingleAsync();
            Check(!UserTime.Resolve(tzId).IsInvalidTime(dueDate.Value), "截止时间位于夏令时跳过的时段");
        }
    }

    private static bool ValidTitle(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200;

    public Task<TodoItemDto> CreateAsync(CreateTodoRequest request) => WriteAsync(async () =>
    {
        await ValidateTaskAsync(request.Title, request.Description, request.Tags, request.Priority,
            request.DueDate, request.DueWarningHours, request.Steps);
        Check(Enum.IsDefined(request.Status) && request.Status != TodoStatus.Frozen, "创建时不能指定冻结状态，请创建后调用冻结操作");
        Check(request.Status != TodoStatus.Done || request.Steps is not { Count: > 0 }, "含未完成步骤的任务不能直接创建为已完成");
        var targetStatus = request.Status;
        request.Status = TodoStatus.NotStarted;
        var result = await todos.CreateAsync(request);
        return targetStatus == TodoStatus.NotStarted ? result : await todos.ChangeStatusAsync(result.Id, targetStatus);
    });

    public Task<TodoItemDto> UpdateAsync(int id, UpdateTodoRequest request) => WriteAsync(async () =>
    {
        await ValidateTaskAsync(request.Title, request.Description, request.Tags, request.Priority,
            request.DueDate, request.DueWarningHours, request.StepsToAdd);
        var task = Found(await todos.GetByIdAsync(id));
        var ids = task.Steps.Select(s => s.Id).ToHashSet();
        Check(request.StepIdsToDelete is null || request.StepIdsToDelete.All(ids.Contains), "删除列表包含不属于该任务的步骤");
        Check(request.StepsOrder is null || request.StepsOrder.Distinct().Count() == request.StepsOrder.Count
            && request.StepsOrder.All(ids.Contains), "排序列表包含重复或不属于该任务的步骤");
        return Found(await todos.UpdateAsync(id, request));
    });

    public Task<bool> DeleteAsync(int id) => WriteAsync(async () =>
        await todos.DeleteAsync(id) ? true : throw new ExternalApiException(404, "任务不存在"));

    public Task<TodoItemDto> ChangeStatusAsync(int id, ChangeStatusRequest request) => WriteAsync(async () =>
    {
        Check(Enum.IsDefined(request.Status), "无效的任务状态");
        Check(request.FrozenReason is null || request.FrozenReason.Length <= 2000, "冻结原因最多 2000 个字符");
        Check(!(request.Status == TodoStatus.Done && request.ResetSteps), "完成任务时不能同时重置步骤");
        return await todos.ChangeStatusAsync(id, request.Status, request.FrozenReason, request.ResetSteps);
    });

    public Task<TodoItemDto> UnfreezeAsync(int id) => WriteAsync(async () =>
    {
        var task = Found(await todos.GetByIdAsync(id));
        Check(task.Status == TodoStatus.Frozen, "任务当前未冻结");
        return await todos.ChangeStatusAsync(id, task.PreviousStatus ?? TodoStatus.NotStarted);
    });

    public Task<TodoItemDto> ArchiveAsync(int id) => WriteAsync(() => todos.ArchiveAsync(id));

    public Task<TodoItemDto> AddStepAsync(int id, string title) => WriteAsync(async () =>
    {
        Check(ValidTitle(title), "步骤标题需为 1–200 个字符");
        return Found(await todos.AddStepAsync(id, title));
    });

    public Task<TodoItemDto> UpdateStepAsync(int id, int stepId, string title) => WriteAsync(async () =>
    {
        Check(ValidTitle(title), "步骤标题需为 1–200 个字符");
        return Found(await todos.UpdateStepAsync(id, stepId, title));
    });

    public Task<TodoItemDto> DeleteStepAsync(int id, int stepId) => WriteAsync(async () =>
        Found(await todos.DeleteStepAsync(id, stepId)));

    public Task<TodoItemDto> SetStepAsync(int id, int stepId, bool completed) => WriteAsync(async () =>
        Found(await todos.SetStepCompletionAsync(id, stepId, completed)));

    public Task<TodayScheduleDto> TodayAsync()
    {
        Require();
        return schedule.GetTodayAsync();
    }

    public async Task<PagedResult<ScheduleRuleDto>> RulesAsync(int page = 1, int pageSize = 20)
    {
        Require();
        Check(page is >= 1 and <= 1000000 && pageSize is >= 1 and <= 100, "page 需为 1–1000000，pageSize 需为 1–100");
        var rules = await schedule.GetRulesAsync();
        var tzId = await db.Users.Where(u => u.Id == current.UserId).Select(u => u.TimeZoneId).SingleAsync();
        return new(rules.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rules.Count, page, pageSize, tzId);
    }

    public async Task<ScheduleRuleDto> RuleAsync(int id)
    {
        Require();
        return Found((await schedule.GetRulesAsync()).SingleOrDefault(r => r.Id == id));
    }

    private static void ValidateRule(SaveScheduleRuleRequest request)
    {
        Check(ValidTitle(request.Title), "规则标题需为 1–200 个字符");
        Check(Enum.IsDefined(request.Mode), "无效的日程模式");
        Check(request.DaysOfWeek is >= 1 and <= 127, "星期掩码需为 1–127");
        Check(request.Tags is null || request.Tags.Length <= 500, "标签最多 500 个字符");
        Check(request.Steps is not null && request.Steps.Count <= 100 && request.Steps.All(ValidTitle), "步骤最多 100 个，标题需为 1–200 个字符");
    }

    public Task<ScheduleRuleDto> CreateRuleAsync(SaveScheduleRuleRequest request) => WriteAsync(async () =>
    {
        ValidateRule(request);
        return Found(await schedule.CreateRuleAsync(request));
    });

    public Task<ScheduleRuleDto> UpdateRuleAsync(int id, SaveScheduleRuleRequest request) => WriteAsync(async () =>
    {
        ValidateRule(request);
        return Found(await schedule.UpdateRuleAsync(id, request));
    });

    public Task<bool> DeleteRuleAsync(int id) => WriteAsync(async () =>
        await schedule.DeleteRuleAsync(id) ? true : throw new ExternalApiException(404, "规则不存在"));
}
