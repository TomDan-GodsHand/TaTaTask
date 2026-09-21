using Microsoft.EntityFrameworkCore;
using TaTaTask.Client.Services;
using TaTaTask.Data;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Entities;
using TaTaTask.Models.Enums;

namespace TaTaTask.Services;

public class ServerTodoService : ITodoService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public ServerTodoService(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    private int Uid => _current.UserId ?? 0;

    private TimeZoneInfo? _tz;

    /// <summary>用户时区，每个请求作用域内只查一次。所有日历口径与 API 边界换算都用它。</summary>
    private async Task<TimeZoneInfo> GetTzAsync()
    {
        if (_tz is not null) return _tz;
        var id = await _db.Users.Where(u => u.Id == Uid).Select(u => u.TimeZoneId).FirstOrDefaultAsync();
        return _tz = UserTime.Resolve(id);
    }

    public async Task<List<TodoItemDto>> GetBoardAsync(string? search = null)
    {
        await AutoArchiveAsync();

        var query = _db.TodoItems
            .Include(t => t.Steps)
            .Include(t => t.SourceRule)
            .Where(t => t.UserId == Uid && !t.IsArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(t => EF.Functions.Like(t.Title, $"%{s}%")
                || (t.Tags != null && EF.Functions.Like(t.Tags, $"%{s}%")));
        }

        var items = await query.ToListAsync();
        var now = DateTime.UtcNow;

        var withDue = items.Where(t => t.DueDate.HasValue)
            .OrderByDescending(t => Score(t, now))
            .ThenBy(t => t.SortOrder);
        var without = items.Where(t => !t.DueDate.HasValue)
            .OrderByDescending(t => Weight(t.Priority))
            .ThenBy(t => t.SortOrder);

        var tz = await GetTzAsync();
        return withDue.Concat(without).Select(t => ToDto(t, tz)).ToList();
    }

    public async Task<TodoItemDto> CreateAsync(CreateTodoRequest request)
    {
        var maxSort = await _db.TodoItems
            .Where(t => t.UserId == Uid && t.Status == request.Status)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? 0;

        var tz = await GetTzAsync();
        var now = DateTime.UtcNow;
        var item = new TodoItem
        {
            UserId = Uid,
            Title = request.Title.Trim(),
            Status = request.Status,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            Tags = NormalizeTags(request.Tags),
            Priority = request.Priority,
            DueDate = UserTime.ToUtc(request.DueDate, tz),
            DueWarningHours = request.DueWarningHours,
            SortOrder = maxSort + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TodoItems.Add(item);

        if (request.Steps is { Count: > 0 })
        {
            var sort = 0;
            foreach (var stepTitle in request.Steps)
            {
                if (!string.IsNullOrWhiteSpace(stepTitle))
                {
                    item.Steps.Add(new TodoStep
                    {
                        TodoItemId = item.Id,
                        UserId = Uid,
                        Title = stepTitle.Trim(),
                        SortOrder = ++sort,
                    });
                }
            }
        }

        await _db.SaveChangesAsync();
        return ToDto(item, tz);
    }

    public async Task<TodoItemDto?> UpdateAsync(int id, UpdateTodoRequest request)
    {
        var item = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid);
        if (item is null) return null;

        var tz = await GetTzAsync();

        item.Title = request.Title.Trim();
        item.Description = request.Description;
        item.Priority = request.Priority;
        item.Tags = NormalizeTags(request.Tags);
        item.DueDate = UserTime.ToUtc(request.DueDate, tz);
        item.DueWarningHours = request.DueWarningHours;
        item.UpdatedAt = DateTime.UtcNow;

        if (request.StepIdsToDelete is { Count: > 0 })
        {
            var toRemove = item.Steps.Where(s => request.StepIdsToDelete.Contains(s.Id)).ToList();
            _db.TodoSteps.RemoveRange(toRemove);
        }

        if (request.StepsToAdd is { Count: > 0 })
        {
            var sort = item.Steps.Count > 0 ? item.Steps.Max(s => s.SortOrder) : 0;
            foreach (var title in request.StepsToAdd)
            {
                if (!string.IsNullOrWhiteSpace(title))
                {
                    item.Steps.Add(new TodoStep
                    {
                        TodoItemId = item.Id,
                        UserId = Uid,
                        Title = title.Trim(),
                        SortOrder = ++sort,
                    });
                }
            }
        }

        if (request.StepsOrder is { Count: > 0 })
        {
            for (int i = 0; i < request.StepsOrder.Count; i++)
            {
                var step = item.Steps.FirstOrDefault(s => s.Id == request.StepsOrder[i]);
                if (step is not null) step.SortOrder = i;
            }
        }

        await _db.SaveChangesAsync();
        return ToDto(item, tz);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _db.TodoItems.FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid);
        if (item is null) return false;

        await DetachFromScheduleAsync(item);

        _db.TodoItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// §3.8 任务被硬删除时：引用它的日程项保留标题快照并标记「任务已删除」；
    /// 若当天时段还没过，退回「待选」让你补选；已过时段或历史则只留痕。
    /// </summary>
    private async Task DetachFromScheduleAsync(TodoItem item)
    {
        var entries = await _db.ScheduleEntries
            .Where(e => e.TodoItemId == item.Id)
            .ToListAsync();
        if (entries.Count == 0) return;

        var tz = await GetTzAsync();
        var today = UserTime.Today(tz);
        var nowLocal = TimeOnly.FromDateTime(UserTime.ToUser(DateTime.UtcNow, tz));
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            entry.TitleSnapshot = item.Title;
            entry.IsTaskDeleted = true;
            entry.TodoItemId = null;
            entry.UpdatedAt = now;

            var canRePick = entry.Date == today
                && nowLocal <= entry.EndTime
                && entry.State != ScheduleEntryState.Completed;

            if (canRePick)
            {
                entry.State = ScheduleEntryState.Pending;
            }
            else
            {
                entry.HandledAt ??= now;
            }
        }
    }

    public async Task<TodoItemDto> ChangeStatusAsync(int id, TodoStatus status, string? frozenReason = null, bool resetSteps = false)
    {
        var item = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid)
            ?? throw new KeyNotFoundException("任务不存在");

        if (status == TodoStatus.Done && item.Steps.Any(s => !s.IsCompleted))
        {
            throw new InvalidOperationException("所有子步骤完成后才能进入已完成");
        }

        if (status == TodoStatus.Frozen && item.Status != TodoStatus.Frozen)
        {
            item.PreviousStatus = item.Status;
            item.FrozenReason = frozenReason;
            item.FrozeAt = DateTime.UtcNow;
        }
        else if (item.Status == TodoStatus.Frozen && status != TodoStatus.Frozen)
        {
            item.PreviousStatus = null;
            item.FrozenReason = null;
            item.FrozeAt = null;
        }

        if (status == TodoStatus.Done && item.Status != TodoStatus.Done)
        {
            item.DoneAt = DateTime.UtcNow;
        }
        else if (status != TodoStatus.Done)
        {
            item.DoneAt = null;
        }

        if (resetSteps)
        {
            foreach (var step in item.Steps)
            {
                step.IsCompleted = false;
            }
        }

        item.Status = status;
        item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(item, await GetTzAsync());
    }

    public async Task<TodoItemDto?> AddStepAsync(int todoId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == todoId && t.UserId == Uid);
        if (task is null)
        {
            return null;
        }

        var maxSort = task.Steps.Count == 0 ? 0 : task.Steps.Max(s => s.SortOrder);
        task.Steps.Add(new TodoStep
        {
            TodoItemId = task.Id,
            UserId = Uid,
            Title = title.Trim(),
            SortOrder = maxSort + 1,
        });
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(task, await GetTzAsync());
    }

    public async Task<TodoItemDto?> ToggleStepAsync(int todoId, int stepId)
    {
        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == todoId && t.UserId == Uid);
        var step = task?.Steps.FirstOrDefault(s => s.Id == stepId);
        if (task is null || step is null)
        {
            return null;
        }

        step.IsCompleted = !step.IsCompleted;

        if (task.Status == TodoStatus.NotStarted && task.Steps.Any(s => s.IsCompleted))
        {
            task.Status = TodoStatus.InProgress;
        }

        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(task, await GetTzAsync());
    }

    public async Task<TodoItemDto?> DeleteStepAsync(int todoId, int stepId)
    {
        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == todoId && t.UserId == Uid);
        var step = task?.Steps.FirstOrDefault(s => s.Id == stepId);
        if (task is null || step is null)
        {
            return null;
        }

        _db.TodoSteps.Remove(step);
        task.Steps.Remove(step);
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(task, await GetTzAsync());
    }

    public async Task<TodoItemDto?> UpdateStepAsync(int todoId, int stepId, string title)
    {
        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == todoId && t.UserId == Uid);
        var step = task?.Steps.FirstOrDefault(s => s.Id == stepId);
        if (task is null || step is null) return null;

        step.Title = title.Trim();
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(task, await GetTzAsync());
    }

    public async Task ReorderStepsAsync(int todoId, List<int> stepIds)
    {
        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == todoId && t.UserId == Uid);
        if (task is null || stepIds.Count == 0) return;

        for (int i = 0; i < stepIds.Count; i++)
        {
            var step = task.Steps.FirstOrDefault(s => s.Id == stepIds[i]);
            if (step is not null) step.SortOrder = i;
        }
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<TodoItemDto> ArchiveAsync(int id)
    {
        var item = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid)
            ?? throw new KeyNotFoundException("任务不存在");

        if (item.Status != TodoStatus.Done)
        {
            throw new InvalidOperationException("只有已完成的任务可以归档");
        }

        if (!item.IsArchived)
        {
            item.IsArchived = true;
            item.ArchivedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return ToDto(item, await GetTzAsync());
    }

    public async Task<List<TodoItemDto>> GetArchivedAsync(string? tag, DateTime? from, DateTime? to, string? q)
    {
        await AutoArchiveAsync();

        var query = _db.TodoItems.Include(t => t.Steps)
            .Where(t => t.UserId == Uid && t.IsArchived);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var s = tag.Trim();
            query = query.Where(t => t.Tags != null && EF.Functions.Like(t.Tags, $"%{s}%"));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(t => EF.Functions.Like(t.Title, $"%{s}%"));
        }

        // from / to 是用户本地日历日，按用户时区换算成 UTC 区间再比较
        var tz = await GetTzAsync();
        if (from.HasValue)
        {
            var fromUtc = UserTime.DayRangeUtc(DateOnly.FromDateTime(from.Value), tz).StartUtc;
            query = query.Where(t => t.ArchivedAt >= fromUtc);
        }
        if (to.HasValue)
        {
            var toEndUtc = UserTime.DayRangeUtc(DateOnly.FromDateTime(to.Value).AddDays(1), tz).StartUtc;
            query = query.Where(t => t.ArchivedAt < toEndUtc);
        }

        var items = await query.OrderByDescending(t => t.ArchivedAt).ToListAsync();
        return items.Select(t => ToDto(t, tz)).ToList();
    }

    public async Task<UserSettingsDto> GetUserSettingsAsync()
    {
        var user = await _db.Users.FindAsync(Uid);
        if (user is null) return new();
        return new UserSettingsDto
        {
            Username = user.Username,
            DefaultDueWarningHours = user.DefaultDueWarningHours,
            DefaultDueDays = user.DefaultDueDays,
            TimeZoneId = user.TimeZoneId,
        };
    }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var tz = await GetTzAsync();
        var now = DateTime.UtcNow;

        // 全部窗口以用户时区的日历口径计算，再换算成 UTC 区间与库中 UTC 值比较
        var todayLocal = UserTime.Today(tz);
        var daysSinceMonday = ((int)todayLocal.DayOfWeek + 6) % 7; // 周一为一周开始
        var thisMondayLocal = todayLocal.AddDays(-daysSinceMonday);
        var lastMondayLocal = thisMondayLocal.AddDays(-7);

        var thisMondayUtc = UserTime.DayRangeUtc(thisMondayLocal, tz).StartUtc;
        var lastMondayUtc = UserTime.DayRangeUtc(lastMondayLocal, tz).StartUtc;
        var flowCutoffUtc = UserTime.DayRangeUtc(thisMondayLocal.AddDays(-7 * 7), tz).StartUtc; // 覆盖近8周
        var cutoff30Utc = UserTime.DayRangeUtc(todayLocal.AddDays(-30), tz).StartUtc;

        // 一次查询拿流量窗口内的任务（含已归档任务的完成记录）
        var flowItems = await _db.TodoItems
            .Where(t => t.UserId == Uid
                && (t.CreatedAt >= flowCutoffUtc || t.DoneAt >= flowCutoffUtc))
            .Select(t => new { t.CreatedAt, t.DoneAt, t.DueDate })
            .ToListAsync();

        var activeItems = await _db.TodoItems
            .Where(t => t.UserId == Uid && !t.IsArchived
                && t.Status != TodoStatus.Done && t.Status != TodoStatus.Frozen)
            .Select(t => new { t.Id, t.Title, t.Priority, t.Tags, t.DueDate, t.CreatedAt })
            .ToListAsync();

        var dto = new DashboardStatsDto();

        // --- 周流量（近8周） ---
        for (int i = 7; i >= 0; i--)
        {
            var weekStartLocal = thisMondayLocal.AddDays(-7 * i);
            var weekStartUtc = UserTime.DayRangeUtc(weekStartLocal, tz).StartUtc;
            var weekEndUtc = UserTime.DayRangeUtc(weekStartLocal.AddDays(7), tz).StartUtc;
            dto.WeeklyFlow.Add(new WeeklyFlowDto
            {
                WeekLabel = weekStartLocal.ToString("MM/dd"),
                IsCurrent = i == 0,
                Created = flowItems.Count(t => t.CreatedAt >= weekStartUtc && t.CreatedAt < weekEndUtc),
                Done = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= weekStartUtc && t.DoneAt < weekEndUtc),
            });
        }

        // --- 吞吐回顾 ---
        dto.DoneThisWeek = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= thisMondayUtc);
        dto.DoneLastWeek = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= lastMondayUtc && t.DoneAt < thisMondayUtc);

        var doneRecent = flowItems
            .Where(t => t.DoneAt != null && t.DoneAt >= cutoff30Utc)
            .Select(t => new { Done = t.DoneAt!.Value, t.CreatedAt, t.DueDate })
            .ToList();

        dto.DoneLast30 = doneRecent.Count;
        if (doneRecent.Count > 0)
        {
            dto.AvgCycleDays = Math.Round(doneRecent.Average(t => (t.Done - t.CreatedAt).TotalDays), 1);
        }

        var doneWithDue = doneRecent.Where(t => t.DueDate != null).ToList();
        if (doneWithDue.Count > 0)
        {
            var onTime = doneWithDue.Count(t => t.Done <= t.DueDate!.Value);
            var overdueDone = doneWithDue.Where(t => t.Done > t.DueDate!.Value).ToList();
            dto.OnTimeDoneCount = onTime;
            dto.OverdueDoneCount = overdueDone.Count;
            if (overdueDone.Count > 0)
            {
                dto.AvgOverdueDays = Math.Round(overdueDone.Average(t => (t.Done - t.DueDate!.Value).TotalDays), 1);
            }
        }

        // --- 当前逾期（活跃任务） ---
        var overdue = activeItems
            .Where(t => t.DueDate != null && t.DueDate < now)
            .OrderBy(t => t.DueDate)
            .ToList();

        dto.OverdueCount = overdue.Count;
        dto.OverdueItems = overdue.Select(t => new OverdueItemDto
        {
            Id = t.Id,
            Title = t.Title,
            DueDate = UserTime.ToUser(t.DueDate!.Value, tz),
            DaysOverdue = (int)Math.Floor((now - t.DueDate.Value).TotalDays),
        }).ToList();

        // --- 标签分布 ---
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in activeItems)
        {
            if (string.IsNullOrWhiteSpace(t.Tags)) continue;
            foreach (var raw in t.Tags.Split(','))
            {
                var tag = raw.Trim();
                if (tag.Length == 0) continue;
                tagCounts[tag] = tagCounts.TryGetValue(tag, out var c) ? c + 1 : 1;
            }
        }
        dto.TagCounts = tagCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(kv => new TagCountDto { Tag = kv.Key, Count = kv.Value })
            .ToList();

        // --- 优先级分布 ---
        foreach (var t in activeItems)
        {
            var p = Math.Clamp(t.Priority, 0, 4);
            dto.PriorityCounts[p]++;
        }

        // --- 老化任务（创建最久未完成） ---
        dto.AgingItems = activeItems
            .OrderBy(t => t.CreatedAt)
            .Take(5)
            .Select(t => new AgingItemDto
            {
                Id = t.Id,
                Title = t.Title,
                AgeDays = (int)Math.Floor((now - t.CreatedAt).TotalDays),
            })
            .ToList();

        return dto;
    }

    private async Task AutoArchiveAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var stale = await _db.TodoItems
            .Where(t => t.UserId == Uid && !t.IsArchived && t.Status == TodoStatus.Done
                && t.DoneAt != null && t.DoneAt < cutoff)
            .ToListAsync();
        if (stale.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var t in stale)
        {
            t.IsArchived = true;
            t.ArchivedAt = now;
            t.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
    }

    private static double Weight(int priority) => priority switch
    {
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        _ => 0,
    };

    private static double Score(TodoItem t, DateTime now)
    {
        var hours = Math.Max((t.DueDate!.Value - now).TotalHours, 1);
        return Weight(t.Priority) * (1.0 / hours);
    }

    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return null;
        var parts = tags.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    /// <summary>库中 UTC → 用户时区墙钟时间（API 边界的唯一转换点）。</summary>
    private static TodoItemDto ToDto(TodoItem t, TimeZoneInfo tz) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        Priority = t.Priority,
        Tags = t.Tags,
        DueDate = UserTime.ToUser(t.DueDate, tz),
        DueWarningHours = t.DueWarningHours,
        Status = t.Status,
        SortOrder = t.SortOrder,
        CreatedAt = UserTime.ToUser(t.CreatedAt, tz),
        UpdatedAt = UserTime.ToUser(t.UpdatedAt, tz),
        IsArchived = t.IsArchived,
        ArchivedAt = UserTime.ToUser(t.ArchivedAt, tz),
        DoneAt = UserTime.ToUser(t.DoneAt, tz),
        PreviousStatus = t.PreviousStatus,
        FrozenReason = t.FrozenReason,
        FrozeAt = UserTime.ToUser(t.FrozeAt, tz),
        SourceRuleId = t.SourceRuleId,
        SourceRuleTitle = t.SourceRule != null ? t.SourceRule.Title : null,
        Steps = t.Steps.OrderBy(s => s.SortOrder).Select(s => new TodoStepDto
        {
            Id = s.Id,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            DueDate = UserTime.ToUser(s.DueDate, tz),
            SortOrder = s.SortOrder,
        }).ToList(),
    };
}
