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
            Tags = string.IsNullOrWhiteSpace(request.Tags) ? null : request.Tags,
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
        item.Tags = request.Tags;
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

    public async Task<TodoItemDto> ChangeStatusAsync(int id, TodoStatus status, string? frozenReason = null)
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

        var activeItems = await _db.TodoItems
            .Include(t => t.Steps)
            .Where(t => t.UserId == Uid && !t.IsArchived
                && t.Status != TodoStatus.Done && t.Status != TodoStatus.Frozen)
            .ToListAsync();

        var doneToday = await _db.TodoItems
            .CountAsync(t => t.UserId == Uid && t.DoneAt >= today);

        var frozenItems = await _db.TodoItems
            .Where(t => t.UserId == Uid && !t.IsArchived && t.Status == TodoStatus.Frozen
                && t.FrozeAt != null)
            .OrderByDescending(t => t.FrozeAt)
            .ToListAsync();

        var overdueCount = await _db.TodoItems
            .CountAsync(t => t.UserId == Uid && !t.IsArchived
                && t.Status != TodoStatus.Done && t.Status != TodoStatus.Frozen
                && t.DueDate < now);

        var totalSteps = activeItems.Sum(t => t.Steps.Count);
        var completedSteps = activeItems.Sum(t => t.Steps.Count(s => s.IsCompleted));

        var frozenList = frozenItems.Select(t => new FrozenItemDto
        {
            Id = t.Id,
            Title = t.Title,
            DaysFrozen = (int)((now - t.FrozeAt!.Value).TotalDays),
            FrozenReason = t.FrozenReason,
        }).ToList();

        var daily = new List<DailyDoneDto>();
        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var next = day.AddDays(1);
            var count = await _db.TodoItems
                .CountAsync(t => t.UserId == Uid && t.DoneAt >= day && t.DoneAt < next);
            daily.Add(new DailyDoneDto
            {
                Date = day.ToString("MM/dd"),
                Count = count,
            });
        }

        return new DashboardStatsDto
        {
            ActiveCount = activeItems.Count,
            DoneTodayCount = doneToday,
            FrozenCount = frozenItems.Count,
            OverdueCount = overdueCount,
            TotalStepsActive = totalSteps,
            CompletedStepsActive = completedSteps,
            FrozenItems = frozenList,
            DailyDone = daily,
        };
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
