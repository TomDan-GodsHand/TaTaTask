using Microsoft.EntityFrameworkCore;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Enums;

namespace TaTaTask.Services;

public partial class ServerTodoService
{
    public async Task<PagedResult<TodoItemDto>> QueryAsync(TaskQuery request, CancellationToken ct = default)
    {
        await AutoArchiveAsync();
        var query = _db.TodoItems.AsNoTracking().Where(t => t.UserId == Uid);
        if (request.Archived.HasValue) query = query.Where(t => t.IsArchived == request.Archived.Value);
        if (request.Status.HasValue) query = query.Where(t => t.Status == request.Status);
        if (request.Priority.HasValue) query = query.Where(t => t.Priority == request.Priority);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(t => t.Title.Contains(q) || (t.Description != null && t.Description.Contains(q))
                || (t.Tags != null && t.Tags.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            // Stored tags are comma-separated; escape LIKE metacharacters to preserve exact matching.
            var tag = request.Tag.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(t => t.Tags != null && EF.Functions.Like(
                "," + t.Tags.Replace("，", ",") + ",", "%," + tag + ",%", "\\"));
        }
        var total = await query.CountAsync(ct);
        var sorted = request.Sort switch
        {
            "created" => query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
            "priority" => query.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id),
            "due" => query.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id),
        };
        var items = await sorted.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Include(t => t.Steps).Include(t => t.SourceRule).ToListAsync(ct);
        var tz = await GetTzAsync();
        return new(items.Select(t => ToDto(t, tz)).ToList(), total, request.Page, request.PageSize, tz.Id);
    }

    public async Task<TodoItemDto?> GetByIdAsync(int id)
    {
        var item = await _db.TodoItems.AsNoTracking().Include(t => t.Steps).Include(t => t.SourceRule)
            .SingleOrDefaultAsync(t => t.UserId == Uid && t.Id == id);
        return item is null ? null : ToDto(item, await GetTzAsync());
    }

    public async Task<TodoItemDto?> SetStepCompletionAsync(int todoId, int stepId, bool completed)
    {
        var task = await _db.TodoItems.Include(t => t.Steps)
            .SingleOrDefaultAsync(t => t.UserId == Uid && t.Id == todoId);
        var step = task?.Steps.SingleOrDefault(s => s.Id == stepId);
        if (task is null || step is null) return null;
        if (step.IsCompleted == completed) return ToDto(task, await GetTzAsync());
        step.IsCompleted = completed;
        if (task.Status == TodoStatus.NotStarted && completed) task.Status = TodoStatus.InProgress;
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(task, await GetTzAsync());
    }
}
