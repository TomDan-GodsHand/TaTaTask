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

    public async Task<List<TodoItemDto>> GetBoardAsync(string? search = null)
    {
        await AutoArchiveAsync();

        var query = _db.TodoItems
            .Include(t => t.Steps)
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

        return withDue.Concat(without).Select(ToDto).ToList();
    }

    public async Task<TodoItemDto> CreateAsync(CreateTodoRequest request)
    {
        var maxSort = await _db.TodoItems
            .Where(t => t.UserId == Uid && t.Status == request.Status)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? 0;

        var now = DateTime.UtcNow;
        var item = new TodoItem
        {
            UserId = Uid,
            Title = request.Title.Trim(),
            Status = request.Status,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            Tags = NormalizeTags(request.Tags),
            Priority = request.Priority,
            DueDate = request.DueDate,
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
        return ToDto(item);
    }

    public async Task<TodoItemDto?> UpdateAsync(int id, UpdateTodoRequest request)
    {
        var item = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid);
        if (item is null) return null;

        item.Title = request.Title.Trim();
        item.Description = request.Description;
        item.Priority = request.Priority;
        item.Tags = NormalizeTags(request.Tags);
        item.DueDate = request.DueDate;
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
        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _db.TodoItems.FirstOrDefaultAsync(t => t.Id == id && t.UserId == Uid);
        if (item is null) return false;
        _db.TodoItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
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
        return ToDto(item);
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
        return ToDto(task);
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
        return ToDto(task);
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
        return ToDto(task);
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
        return ToDto(task);
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

        return ToDto(item);
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
        if (from.HasValue)
        {
            query = query.Where(t => t.ArchivedAt >= from.Value);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1);
            query = query.Where(t => t.ArchivedAt < end);
        }

        var items = await query.OrderByDescending(t => t.ArchivedAt).ToListAsync();
        return items.Select(ToDto).ToList();
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
        };
    }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // 周一为一周开始
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-daysSinceMonday);
        var lastMonday = thisMonday.AddDays(-7);
        var flowCutoff = thisMonday.AddDays(-7 * 7); // 覆盖近8周
        var cutoff30 = today.AddDays(-30);

        // 一次查询拿流量窗口内的任务（含已归档任务的完成记录）
        var flowItems = await _db.TodoItems
            .Where(t => t.UserId == Uid
                && (t.CreatedAt >= flowCutoff || t.DoneAt >= flowCutoff))
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
            var weekStart = thisMonday.AddDays(-7 * i);
            var weekEnd = weekStart.AddDays(7);
            dto.WeeklyFlow.Add(new WeeklyFlowDto
            {
                WeekLabel = weekStart.ToString("MM/dd"),
                IsCurrent = i == 0,
                Created = flowItems.Count(t => t.CreatedAt >= weekStart && t.CreatedAt < weekEnd),
                Done = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= weekStart && t.DoneAt < weekEnd),
            });
        }

        // --- 吞吐回顾 ---
        dto.DoneThisWeek = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= thisMonday);
        dto.DoneLastWeek = flowItems.Count(t => t.DoneAt != null && t.DoneAt >= lastMonday && t.DoneAt < thisMonday);

        var doneRecent = flowItems
            .Where(t => t.DoneAt != null && t.DoneAt >= cutoff30)
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
            DueDate = t.DueDate!.Value,
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

    private static TodoItemDto ToDto(TodoItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        Priority = t.Priority,
        Tags = t.Tags,
        DueDate = t.DueDate,
        DueWarningHours = t.DueWarningHours,
        Status = t.Status,
        SortOrder = t.SortOrder,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        IsArchived = t.IsArchived,
        ArchivedAt = t.ArchivedAt,
        DoneAt = t.DoneAt,
        PreviousStatus = t.PreviousStatus,
        FrozenReason = t.FrozenReason,
        FrozeAt = t.FrozeAt,
        Steps = t.Steps.OrderBy(s => s.SortOrder).Select(s => new TodoStepDto
        {
            Id = s.Id,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            DueDate = s.DueDate,
            SortOrder = s.SortOrder,
        }).ToList(),
    };
}
