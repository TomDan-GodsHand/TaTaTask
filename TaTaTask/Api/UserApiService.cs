using Microsoft.EntityFrameworkCore;
using TaTaTask.Client.Services;
using TaTaTask.Data;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Models.Enums;

namespace TaTaTask.Api;

/// <summary>当前用户、标签与统计。</summary>
public sealed class UserApiService
{
    private readonly AppDbContext _db;
    private readonly ApiContext _ctx;
    private readonly ITodoService _todos;
    private readonly IScheduleService _schedule;

    public UserApiService(AppDbContext db, ApiContext ctx, ITodoService todos, IScheduleService schedule)
    {
        _db = db;
        _ctx = ctx;
        _todos = todos;
        _schedule = schedule;
    }

    public async Task<MeDto> GetMeAsync()
    {
        var tz = await _ctx.GetTimeZoneAsync();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _ctx.UserId)
            ?? throw ApiException.NotFound("用户不存在");

        return new MeDto
        {
            Id = user.Id,
            Username = user.Username,
            IsAdmin = user.IsAdmin,
            TimeZoneId = await _ctx.GetTimeZoneIdAsync(),
            DefaultDueWarningHours = user.DefaultDueWarningHours,
            DefaultDueDays = user.DefaultDueDays,
            CreatedAt = ApiTime.ToOffset(user.CreatedAt, tz),
            TokenName = _ctx.TokenName,
            CanWrite = _ctx.CanWrite,
        };
    }

    public async Task<List<TagCountApiDto>> GetTagsAsync(bool includeArchived)
    {
        var rows = await _db.TodoItems.AsNoTracking()
            .Where(t => t.UserId == _ctx.UserId && (includeArchived || !t.IsArchived))
            .Select(t => t.Tags)
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var tag in ApiEnums.ParseTags(row))
                counts[tag] = counts.TryGetValue(tag, out var count) ? count + 1 : 1;
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new TagCountApiDto { Tag = kv.Key, Count = kv.Value })
            .ToList();
    }

    public async Task<StatsApiDto> GetStatsAsync()
    {
        var tz = await _ctx.GetTimeZoneAsync();
        var board = await _todos.GetStatsAsync();
        var schedule = await _schedule.GetStatsAsync();

        var activeCount = await _db.TodoItems.CountAsync(t => t.UserId == _ctx.UserId
            && !t.IsArchived && t.Status != TodoStatus.Done && t.Status != TodoStatus.Frozen);

        return new StatsApiDto
        {
            ActiveCount = activeCount,
            OverdueCount = board.OverdueCount,
            DoneThisWeek = board.DoneThisWeek,
            DoneLastWeek = board.DoneLastWeek,
            DoneLast30 = board.DoneLast30,
            AvgCycleDays = board.AvgCycleDays,
            OnTimeDoneCount = board.OnTimeDoneCount,
            OverdueDoneCount = board.OverdueDoneCount,
            AvgOverdueDays = board.AvgOverdueDays,
            PriorityCounts = board.PriorityCounts,
            WeeklyFlow = board.WeeklyFlow.Select(w => new WeeklyFlowApiDto
            {
                WeekLabel = w.WeekLabel,
                IsCurrent = w.IsCurrent,
                Created = w.Created,
                Done = w.Done,
            }).ToList(),
            TagCounts = board.TagCounts.Select(t => new TagCountApiDto { Tag = t.Tag, Count = t.Count }).ToList(),
            OverdueTasks = board.OverdueItems.Select(o => new OverdueTaskApiDto
            {
                Id = o.Id,
                Title = o.Title,
                DueDate = ApiTime.WallClockToOffset(o.DueDate, tz),
                DaysOverdue = o.DaysOverdue,
            }).ToList(),
            AgingTasks = board.AgingItems.Select(a => new AgingTaskApiDto
            {
                Id = a.Id,
                Title = a.Title,
                AgeDays = a.AgeDays,
            }).ToList(),
            Schedule = schedule.HasData
                ? new ScheduleSummaryApiDto
                {
                    TodayDone = schedule.TodayDone,
                    TodayPlanned = schedule.TodayPlanned,
                    Last30Done = schedule.Last30Done,
                    Last30Planned = schedule.Last30Planned,
                    Last30Rate = schedule.Last30Rate,
                    StreakDays = schedule.StreakDays,
                    AvgDailyMinutes = schedule.AvgDailyMinutes,
                }
                : null,
        };
    }
}
