using Microsoft.EntityFrameworkCore;
using TaTaTask.Client.Services;
using TaTaTask.Data;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Models.Entities;
using TaTaTask.Models.Enums;
using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>任务列表的查询参数。</summary>
public class TaskListQuery
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>notStarted / inProgress / converging / done / frozen；不传表示全部。</summary>
    public string? Status { get; set; }

    /// <summary>完整标签精确匹配（不是模糊匹配）。</summary>
    public string? Tag { get; set; }

    /// <summary>标题、描述、标签的模糊搜索。</summary>
    public string? Q { get; set; }

    public int? Priority { get; set; }

    /// <summary>是否只看归档任务；默认 false。传 null 表示不筛选。</summary>
    public bool? Archived { get; set; } = false;

    public DateTimeOffset? DueFrom { get; set; }
    public DateTimeOffset? DueTo { get; set; }

    /// <summary>updated（默认倒序）、created、due、priority、sortOrder；前缀 - 表示倒序。</summary>
    public string? Sort { get; set; }
}

/// <summary>
/// 对外任务接口。查询走本项目自己的分页查询；写操作复用 <see cref="ITodoService"/>，
/// 以免绕过「步骤门禁」「冻结/解冻」「自动归档」等既有业务规则。
/// </summary>
public sealed class TaskApiService
{
    private readonly AppDbContext _db;
    private readonly ServerTodoService _todos;
    private readonly ApiContext _ctx;
    private readonly ApiCommandExecutor _commands;

    public TaskApiService(
        AppDbContext db,
        ServerTodoService todos,
        ApiContext ctx,
        ApiCommandExecutor commands)
    {
        _db = db;
        _todos = todos;
        _ctx = ctx;
        _commands = commands;
    }

    public async Task<PagedResult<TaskDto>> ListAsync(TaskListQuery query, CancellationToken ct)
    {
        var tz = await _ctx.GetTimeZoneAsync();
        var (page, pageSize) = PageQuery.Normalize(query.Page, query.PageSize);
        if (query.DueFrom > query.DueTo) throw ApiException.BadRequest("dueTo 不能早于 dueFrom");
        if (query.Tag?.IndexOfAny([',', '，']) >= 0) throw ApiException.BadRequest("tag 只接受一个完整标签");
        await _todos.AutoArchiveAsync();

        var source = _db.TodoItems.AsNoTracking()
            .Include(t => t.Steps)
            .Include(t => t.SourceRule)
            .Where(t => t.UserId == _ctx.UserId);

        if (query.Archived.HasValue)
            source = source.Where(t => t.IsArchived == query.Archived.Value);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!ApiEnums.TryParseTodoStatus(query.Status, out var status))
                throw ApiException.BadRequest("无效的任务状态");
            source = source.Where(t => t.Status == status);
        }
        if (query.Priority.HasValue)
        {
            if (query.Priority is < 0 or > 4) throw ApiException.BadRequest("优先级需为 0–4");
            source = source.Where(t => t.Priority == query.Priority.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = EscapeLike(query.Tag.Trim());
            source = source.Where(t => t.Tags != null
                && EF.Functions.Like("," + t.Tags.Replace("，", ",") + ",", "%," + tag + ",%", "\\"));
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var text = EscapeLike(query.Q.Trim());
            source = source.Where(t => EF.Functions.Like(t.Title, "%" + text + "%", "\\")
                || (t.Description != null && EF.Functions.Like(t.Description, "%" + text + "%", "\\"))
                || (t.Tags != null && EF.Functions.Like(t.Tags, "%" + text + "%", "\\")));
        }
        if (query.DueFrom.HasValue)
        {
            var from = ApiTime.ToUtc(query.DueFrom.Value);
            source = source.Where(t => t.DueDate != null && t.DueDate >= from);
        }
        if (query.DueTo.HasValue)
        {
            var to = ApiTime.ToUtc(query.DueTo.Value);
            source = source.Where(t => t.DueDate != null && t.DueDate <= to);
        }

        var total = await source.CountAsync(ct);
        var sorted = ApplySort(source, query.Sort);
        var items = await sorted.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return PagedResult<TaskDto>.Create(items.Select(t => ToDto(t, tz)).ToList(), page, pageSize, total);
    }

    public async Task<TaskDto> GetAsync(int id)
    {
        var tz = await _ctx.GetTimeZoneAsync();
        return ToDto(await GetEntityAsync(id), tz);
    }

    public Task<TaskDto> CreateAsync(CreateTaskRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var title = ValidateTitle(request.Title);
            ValidateDescription(request.Description);
            ValidatePriority(request.Priority);
            ValidateWarningHours(request.DueWarningHours);
            var tags = ApiEnums.JoinTags(request.Tags);
            ValidateTags(tags);
            var steps = ValidateSteps(request.Steps);

            var status = TodoStatus.NotStarted;
            if (!string.IsNullOrWhiteSpace(request.Status) && !ApiEnums.TryParseTodoStatus(request.Status, out status))
                throw ApiException.BadRequest("无效的任务状态");
            if (status == TodoStatus.Frozen)
                throw ApiException.BadRequest("创建任务时不能指定冻结状态，请创建后再冻结");
            if (status == TodoStatus.Done && steps.Count > 0)
                throw ApiException.BadRequest("带子步骤的任务不能直接创建为已完成");

            var tz = await _ctx.GetTimeZoneAsync();
            var created = await _todos.CreateAsync(new CreateTodoRequest
            {
                Title = title,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
                Priority = request.Priority,
                Tags = tags,
                DueDate = null,
                DueWarningHours = request.DueWarningHours,
                Status = TodoStatus.NotStarted,
                Steps = steps.Count > 0 ? steps : null,
            });

            // 内部创建接口只接受墙钟时间；保留外部请求的确切时刻，避免夏令时回拨歧义。
            if (request.DueDate.HasValue)
            {
                var entity = await _db.TodoItems.SingleAsync(t => t.Id == created.Id && t.UserId == _ctx.UserId);
                entity.DueDate = ApiTime.ToUtc(request.DueDate.Value);
                await _db.SaveChangesAsync();
            }

            if (status != TodoStatus.NotStarted)
                created = await _todos.ChangeStatusAsync(created.Id, status);

            return ToDto(await GetEntityAsync(created.Id), tz);
        });

    public Task<TaskDto> PatchAsync(int id, PatchTaskRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var item = await _db.TodoItems.Include(t => t.Steps)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _ctx.UserId)
                ?? throw ApiException.NotFound("任务不存在");

            if (request.Title is not null) item.Title = ValidateTitle(request.Title);
            if (request.Description is not null)
                item.Description = string.IsNullOrEmpty(request.Description) ? null : ValidateDescription(request.Description);
            if (request.Priority.HasValue)
            {
                ValidatePriority(request.Priority.Value);
                item.Priority = request.Priority.Value;
            }
            if (request.Tags is not null)
            {
                item.Tags = ApiEnums.JoinTags(request.Tags);
                ValidateTags(item.Tags);
            }
            if (request.ClearDueDate)
            {
                item.DueDate = null;
            }
            else if (request.DueDate.HasValue)
            {
                item.DueDate = ApiTime.ToUtc(request.DueDate.Value);
            }
            if (request.DueWarningHours.HasValue)
            {
                ValidateWarningHours(request.DueWarningHours);
                item.DueWarningHours = request.DueWarningHours;
            }

            var stepIds = item.Steps.Select(s => s.Id).ToHashSet();
            if (request.StepIdsToDelete is { Count: > 0 })
            {
                if (request.StepIdsToDelete.Any(s => !stepIds.Contains(s)))
                    throw ApiException.BadRequest("要删除的子步骤不属于该任务");
                var removing = item.Steps.Where(s => request.StepIdsToDelete.Contains(s.Id)).ToList();
                foreach (var step in removing) item.Steps.Remove(step);
            }
            if (request.StepsToAdd is { Count: > 0 })
            {
                var additions = ValidateSteps(request.StepsToAdd);
                if (item.Steps.Count + additions.Count > 100) throw ApiException.BadRequest("子步骤最多 100 个");
                var sort = item.Steps.Count == 0 ? 0 : item.Steps.Max(s => s.SortOrder);
                foreach (var stepTitle in additions)
                {
                    item.Steps.Add(new TodoStep
                    {
                        TodoItemId = item.Id,
                        UserId = item.UserId,
                        Title = stepTitle,
                        SortOrder = ++sort,
                    });
                }
            }
            if (request.StepsOrder is { Count: > 0 })
            {
                var ordered = request.StepsOrder.Distinct().ToList();
                var remaining = item.Steps.Where(s => s.Id != 0).Select(s => s.Id).ToHashSet();
                if (ordered.Count != request.StepsOrder.Count || !remaining.SetEquals(ordered))
                    throw ApiException.BadRequest("排序列表必须完整包含保留的已有子步骤，且不得重复");
                for (var i = 0; i < ordered.Count; i++)
                {
                    var step = item.Steps.FirstOrDefault(s => s.Id == ordered[i]);
                    if (step is not null) step.SortOrder = i;
                }
            }

            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return ToDto(item, await _ctx.GetTimeZoneAsync());
        });

    public Task DeleteAsync(int id)
        => _commands.ExecuteAsync(async () =>
        {
            if (!await _todos.DeleteAsync(id)) throw ApiException.NotFound("任务不存在");
        });

    public Task<TaskDto> ChangeStatusAsync(int id, ChangeTaskStatusRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            if (!ApiEnums.TryParseTodoStatus(request.Status, out var status))
                throw ApiException.BadRequest("无效的任务状态");
            if (request.FrozenReason is { Length: > 2000 })
                throw ApiException.BadRequest("冻结原因最多 2000 个字符");
            if (status == TodoStatus.Done && request.ResetSteps)
                throw ApiException.BadRequest("完成任务时不能同时重置子步骤");

            var updated = await _todos.ChangeStatusAsync(id, status, request.FrozenReason, request.ResetSteps);
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> ArchiveAsync(int id)
        => _commands.ExecuteAsync(async () =>
        {
            var updated = await _todos.ArchiveAsync(id);
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> UnfreezeAsync(int id) => _commands.ExecuteAsync(async () =>
        {
            var item = await GetEntityAsync(id);
            if (item.Status != TodoStatus.Frozen) throw ApiException.Conflict("任务当前未冻结");
            await _todos.ChangeStatusAsync(id, item.PreviousStatus ?? TodoStatus.NotStarted);
            return ToDto(await GetEntityAsync(id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> UnarchiveAsync(int id)
        => _commands.ExecuteAsync(async () =>
        {
            var tz = await _ctx.GetTimeZoneAsync();
            var item = await _db.TodoItems.Include(t => t.Steps).Include(t => t.SourceRule)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _ctx.UserId)
                ?? throw ApiException.NotFound("任务不存在");

            if (item.IsArchived)
            {
                item.IsArchived = false;
                item.ArchivedAt = null;
                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return ToDto(item, tz);
        });

    public Task<TaskDto> AddStepAsync(int id, string title)
        => _commands.ExecuteAsync(async () =>
        {
            var task = await GetEntityAsync(id);
            if (task.Steps.Count >= 100) throw ApiException.BadRequest("子步骤最多 100 个");
            var updated = await _todos.AddStepAsync(id, ValidateTitle(title))
                ?? throw ApiException.NotFound("任务不存在");
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> UpdateStepAsync(int id, int stepId, string title)
        => _commands.ExecuteAsync(async () =>
        {
            var updated = await _todos.UpdateStepAsync(id, stepId, ValidateTitle(title))
                ?? throw ApiException.NotFound("任务或子步骤不存在");
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> DeleteStepAsync(int id, int stepId)
        => _commands.ExecuteAsync(async () =>
        {
            var updated = await _todos.DeleteStepAsync(id, stepId)
                ?? throw ApiException.NotFound("任务或子步骤不存在");
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> ToggleStepAsync(int id, int stepId)
        => _commands.ExecuteAsync(async () =>
        {
            var updated = await _todos.ToggleStepAsync(id, stepId)
                ?? throw ApiException.NotFound("任务或子步骤不存在");
            return ToDto(await GetEntityAsync(updated.Id), await _ctx.GetTimeZoneAsync());
        });

    public Task<TaskDto> SetStepCompletionAsync(int id, int stepId, bool completed)
        => _commands.ExecuteAsync(async () =>
        {
            var task = await _db.TodoItems.Include(t => t.Steps)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _ctx.UserId)
                ?? throw ApiException.NotFound("任务不存在");
            var step = task.Steps.FirstOrDefault(s => s.Id == stepId)
                ?? throw ApiException.NotFound("子步骤不存在");

            if (step.IsCompleted != completed)
            {
                step.IsCompleted = completed;
                if (task.Status == TodoStatus.NotStarted && completed)
                    task.Status = TodoStatus.InProgress;
                task.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return ToDto(task, await _ctx.GetTimeZoneAsync());
        });

    public Task ReorderStepsAsync(int id, List<int> stepIds)
        => _commands.ExecuteAsync(async () =>
        {
            var task = await _db.TodoItems.Include(t => t.Steps)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _ctx.UserId)
                ?? throw ApiException.NotFound("任务不存在");

            var distinct = stepIds.Distinct().ToList();
            if (distinct.Count != stepIds.Count || !task.Steps.Select(s => s.Id).ToHashSet().SetEquals(distinct))
                throw ApiException.BadRequest("排序列表必须完整包含该任务的全部子步骤，且不得重复");

            for (var i = 0; i < distinct.Count; i++)
            {
                var step = task.Steps.First(x => x.Id == distinct[i]);
                step.SortOrder = i;
            }
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        });

    private IQueryable<TodoItem> ApplySort(IQueryable<TodoItem> source, string? sort)
    {
        var key = (sort ?? "-updated").Trim();
        var descending = key.StartsWith('-');
        var field = descending ? key[1..] : key;

        return field.ToLowerInvariant() switch
        {
            "updated" or "updatedat" => descending
                ? source.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id)
                : source.OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id),
            "created" or "createdat" => descending
                ? source.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : source.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            "due" or "duedate" => descending
                ? source.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate).ThenByDescending(t => t.Id)
                : source.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Id),
            "priority" => descending
                ? source.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id)
                : source.OrderBy(t => t.Priority).ThenBy(t => t.Id),
            "sortorder" or "sort" => descending
                ? source.OrderByDescending(t => t.SortOrder).ThenByDescending(t => t.Id)
                : source.OrderBy(t => t.SortOrder).ThenBy(t => t.Id),
            _ => throw ApiException.BadRequest("sort 需为 updated、created、due、priority 或 sortOrder，前缀 - 表示倒序"),
        };
    }

    private async Task<TodoItem> GetEntityAsync(int id)
        => await _db.TodoItems.AsNoTracking()
            .Include(t => t.Steps)
            .Include(t => t.SourceRule)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _ctx.UserId)
            ?? throw ApiException.NotFound("任务不存在");

    private static TaskDto ToDto(TodoItem t, TimeZoneInfo tz) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        Priority = t.Priority,
        Tags = ApiEnums.ParseTags(t.Tags),
        DueDate = ApiTime.ToOffset(t.DueDate, tz),
        DueWarningHours = t.DueWarningHours,
        Status = ApiEnums.StatusName(t.Status),
        SortOrder = t.SortOrder,
        CreatedAt = ApiTime.ToOffset(t.CreatedAt, tz),
        UpdatedAt = ApiTime.ToOffset(t.UpdatedAt, tz),
        IsArchived = t.IsArchived,
        ArchivedAt = ApiTime.ToOffset(t.ArchivedAt, tz),
        DoneAt = ApiTime.ToOffset(t.DoneAt, tz),
        PreviousStatus = t.PreviousStatus.HasValue ? ApiEnums.StatusName(t.PreviousStatus.Value) : null,
        FrozenReason = t.FrozenReason,
        FrozeAt = ApiTime.ToOffset(t.FrozeAt, tz),
        SourceRuleId = t.SourceRuleId,
        SourceRuleTitle = t.SourceRule?.Title,
        Steps = t.Steps.OrderBy(s => s.SortOrder).Select(s => new TaskStepDto
        {
            Id = s.Id,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            DueDate = ApiTime.ToOffset(s.DueDate, tz),
            SortOrder = s.SortOrder,
        }).ToList(),
    };

    private static string ValidateTitle(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200)
            throw ApiException.BadRequest("标题需为 1–200 个字符");
        return value;
    }

    private static string ValidateDescription(string? description)
    {
        if (description is { Length: > 2000 })
            throw ApiException.BadRequest("描述最多 2000 个字符");
        return description!;
    }

    private static void ValidatePriority(int priority)
    {
        if (priority is < 0 or > 4)
            throw ApiException.BadRequest("优先级需为 0–4");
    }

    private static void ValidateWarningHours(int? hours)
    {
        if (hours is < 0)
            throw ApiException.BadRequest("提醒小时数不能为负数");
    }

    private static void ValidateTags(string? tags)
    {
        if (tags is { Length: > 500 })
            throw ApiException.BadRequest("标签总长度最多 500 个字符");
    }

    private static List<string> ValidateSteps(List<string>? steps)
    {
        if (steps is null) return new List<string>();
        if (steps.Count > 100) throw ApiException.BadRequest("子步骤最多 100 个");
        return steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => ValidateTitle(s)).ToList();
    }

    private static string EscapeLike(string text)
        => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

}
