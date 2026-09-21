using Microsoft.EntityFrameworkCore;
using TaTaTask.Client.Services;
using TaTaTask.Data;
using TaTaTask.Models;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Entities;
using TaTaTask.Models.Enums;

namespace TaTaTask.Services;

/// <summary>
/// 日程服务端实现：周规则 CRUD、每日惰性物化、今日视图、子步骤推进、未完成处置、历史。
/// 「今天」一律按用户时区判定（见 <see cref="UserTime"/>）。
/// </summary>
public class ServerScheduleService : IScheduleService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public ServerScheduleService(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    private int Uid => _current.UserId ?? 0;

    private TimeZoneInfo? _tz;
    private string? _tzId;

    private async Task<(TimeZoneInfo Tz, string Id)> GetTzAsync()
    {
        if (_tz is not null) return (_tz, _tzId!);
        var id = await _db.Users.Where(u => u.Id == Uid).Select(u => u.TimeZoneId).FirstOrDefaultAsync();
        _tzId = string.IsNullOrWhiteSpace(id) ? UserTime.DefaultTimeZoneId : id;
        _tz = UserTime.Resolve(_tzId);
        return (_tz, _tzId);
    }

    // ==================== 周规则 ====================

    public async Task<List<ScheduleRuleDto>> GetRulesAsync()
    {
        var rules = await _db.ScheduleRules.Include(r => r.Steps)
            .Where(r => r.UserId == Uid)
            .OrderBy(r => r.StartTime).ThenBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();
        return rules.Select(ToRuleDto).ToList();
    }

    public async Task<ScheduleRuleDto?> CreateRuleAsync(SaveScheduleRuleRequest request)
    {
        Validate(request);

        var now = DateTime.UtcNow;
        var rule = new ScheduleRule
        {
            UserId = Uid,
            Title = request.Title.Trim(),
            Mode = request.Mode,
            DaysOfWeek = request.DaysOfWeek & ScheduleDays.All,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Tags = TagUtil.Normalize(request.Tags),
            IsActive = request.IsActive,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplyTemplateSteps(rule, request.Steps);

        _db.ScheduleRules.Add(rule);
        await _db.SaveChangesAsync();
        return ToRuleDto(rule);
    }

    public async Task<ScheduleRuleDto?> UpdateRuleAsync(int id, SaveScheduleRuleRequest request)
    {
        Validate(request);

        var rule = await _db.ScheduleRules.Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == Uid);
        if (rule is null) return null;

        rule.Title = request.Title.Trim();
        rule.Mode = request.Mode;
        rule.DaysOfWeek = request.DaysOfWeek & ScheduleDays.All;
        rule.StartTime = request.StartTime;
        rule.EndTime = request.EndTime;
        rule.Tags = TagUtil.Normalize(request.Tags);
        rule.IsActive = request.IsActive;
        rule.StartDate = request.StartDate;
        rule.EndDate = request.EndDate;
        rule.SortOrder = request.SortOrder;
        rule.UpdatedAt = DateTime.UtcNow;

        ApplyTemplateSteps(rule, request.Steps);

        await _db.SaveChangesAsync();
        return ToRuleDto(rule);
    }

    public async Task<bool> DeleteRuleAsync(int id)
    {
        var rule = await _db.ScheduleRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == Uid);
        if (rule is null) return false;

        // 历史日程项保留（FK 为 SetNull），生成的卡也原样留在看板
        _db.ScheduleRules.Remove(rule);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Validate(SaveScheduleRuleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new InvalidOperationException("标题不能为空");
        if ((r.DaysOfWeek & ScheduleDays.All) == 0) throw new InvalidOperationException("至少要选择一天");
        if (r.EndTime <= r.StartTime) throw new InvalidOperationException("结束时间必须晚于开始时间");
        if (r.Mode == ScheduleRuleMode.Pick && TagUtil.Split(r.Tags).Length == 0)
            throw new InvalidOperationException("挑选模式必须至少设置一个标签");
        if (r.StartDate is { } sd && r.EndDate is { } ed && ed < sd)
            throw new InvalidOperationException("结束日期不能早于开始日期");
    }

    private static void ApplyTemplateSteps(ScheduleRule rule, List<string> steps)
    {
        rule.Steps.Clear();
        var order = 0;
        foreach (var title in steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
        {
            rule.Steps.Add(new ScheduleRuleStep { Title = title, SortOrder = ++order });
        }
    }

    // ==================== 今日日程 ====================

    public async Task<TodayScheduleDto> GetTodayAsync()
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        await MaterializeAsync(today);
        return await BuildTodayAsync(today, tz, tzId);
    }

    /// <summary>每日物化：把今天命中的规则补齐成日程项。以 (UserId, Date, RuleId) 唯一索引保证幂等。</summary>
    private async Task MaterializeAsync(DateOnly date)
    {
        var dowBit = ScheduleDays.Bit(date.DayOfWeek);
        var rules = await _db.ScheduleRules.Include(r => r.Steps)
            .Where(r => r.UserId == Uid && r.IsActive
                && (r.DaysOfWeek & dowBit) != 0
                && (r.StartDate == null || r.StartDate <= date)
                && (r.EndDate == null || r.EndDate >= date))
            .OrderBy(r => r.StartTime).ThenBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();
        if (rules.Count == 0) return;

        var existingEntries = await _db.ScheduleEntries
            .Include(e => e.Steps).ThenInclude(s => s.TodoStep)
            .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
            .Where(e => e.UserId == Uid && e.Date == date && e.RuleId != null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var ruleById = rules.ToDictionary(r => r.Id);
        var dirty = false;

        // 规则改了时段 → 把今天**还没动过**的条目同步成新时间；
        // 已经推进过（勾过步骤 / 任务已开始 / 已完成）的条目保留当时的时间，不改历史事实。
        foreach (var entry in existingEntries)
        {
            if (entry.RuleId is not { } rid || !ruleById.TryGetValue(rid, out var rule)) continue;
            if (entry.StartTime == rule.StartTime && entry.EndTime == rule.EndTime) continue;
            if (!IsUntouched(entry)) continue;

            entry.StartTime = rule.StartTime;
            entry.EndTime = rule.EndTime;
            entry.UpdatedAt = now;
            dirty = true;
        }

        var missing = rules.Where(r => existingEntries.All(e => e.RuleId != r.Id)).ToList();
        if (missing.Count == 0)
        {
            if (dirty) await _db.SaveChangesAsync();
            return;
        }

        var maxSort = await _db.TodoItems
            .Where(t => t.UserId == Uid && t.Status == TodoStatus.NotStarted)
            .Select(t => (int?)t.SortOrder).MaxAsync() ?? 0;

        foreach (var rule in missing)
        {
            var entry = new ScheduleEntry
            {
                UserId = Uid,
                Date = date,
                RuleId = rule.Id,
                StartTime = rule.StartTime,
                EndTime = rule.EndTime,
                CreatedAt = now,
                UpdatedAt = now,
            };

            if (rule.Mode == ScheduleRuleMode.Generate)
            {
                // 自动生成一张看板卡（带规则标签 + 步骤模板），再排进该时段
                var task = new TodoItem
                {
                    UserId = Uid,
                    Title = rule.Title,
                    Tags = TagUtil.Normalize(rule.Tags),
                    Status = TodoStatus.NotStarted,
                    SourceRuleId = rule.Id,
                    SortOrder = ++maxSort,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var order = 0;
                foreach (var tpl in rule.Steps.OrderBy(s => s.SortOrder))
                {
                    var step = new TodoStep { UserId = Uid, Title = tpl.Title, SortOrder = ++order };
                    task.Steps.Add(step);
                    entry.Steps.Add(new ScheduleEntryStep { TodoStep = step, SortOrder = order });
                }

                entry.TodoItem = task;
                entry.TitleSnapshot = task.Title;
                entry.State = ScheduleEntryState.Scheduled;
            }
            else
            {
                entry.State = ScheduleEntryState.Pending;
            }

            _db.ScheduleEntries.Add(entry);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>今天的条目是否「没动过」：没完成、没跳过、没勾过步骤、任务也还没开始。</summary>
    private static bool IsUntouched(ScheduleEntry entry)
    {
        if (entry.State is ScheduleEntryState.Completed or ScheduleEntryState.Skipped) return false;
        if (entry.Steps.Any(s => s.TodoStep?.IsCompleted == true)) return false;
        if (entry.TodoItem is { } task && task.Status != TodoStatus.NotStarted) return false;
        return true;
    }

    private IQueryable<ScheduleEntry> EntryQuery() => _db.ScheduleEntries
        .Include(e => e.Rule)
        .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
        .Include(e => e.Steps).ThenInclude(s => s.TodoStep);

    private async Task<TodayScheduleDto> BuildTodayAsync(DateOnly date, TimeZoneInfo tz, string tzId)
    {
        var entries = await EntryQuery()
            .Where(e => e.UserId == Uid && e.Date == date)
            .OrderBy(e => e.StartTime).ThenBy(e => e.Id)
            .ToListAsync();

        var unfinished = await EntryQuery()
            .Where(e => e.UserId == Uid && e.Date < date && e.HandledAt == null
                && (e.State == ScheduleEntryState.Pending || e.State == ScheduleEntryState.Scheduled))
            .OrderByDescending(e => e.Date).ThenBy(e => e.StartTime)
            .Take(50)
            .ToListAsync();

        var dto = new TodayScheduleDto
        {
            Date = date,
            TimeZoneId = tzId,
            Entries = entries.Select(e => ToEntryDto(e, tz)).ToList(),
            Unfinished = unfinished.Select(e => ToEntryDto(e, tz)).ToList(),
        };

        var nowTime = TimeOnly.FromDateTime(UserTime.ToUser(DateTime.UtcNow, tz));
        dto.NowTime = nowTime;
        dto.CurrentEntryId = dto.Entries.FirstOrDefault(e =>
            e.StartTime <= nowTime && nowTime < e.EndTime
            && (e.State == ScheduleEntryState.Pending || e.State == ScheduleEntryState.Scheduled))?.Id;

        return dto;
    }

    // ==================== 挑选模式 ====================

    public async Task<List<ScheduleCandidateDto>> GetCandidatesAsync(int entryId)
    {
        var entry = await _db.ScheduleEntries.Include(e => e.Rule)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry is null) return [];

        var wanted = TagUtil.Split(entry.Rule?.Tags);

        var tasks = await _db.TodoItems.Include(t => t.Steps)
            .Where(t => t.UserId == Uid && !t.IsArchived
                && t.Status != TodoStatus.Done && t.Status != TodoStatus.Frozen)
            .ToListAsync();

        // 整标签精确匹配（在内存里做，避免 LIKE 把「工作」命中「工作A」）
        if (wanted.Length > 0)
        {
            tasks = tasks.Where(t => TagUtil.ContainsAny(t.Tags, wanted)).ToList();
        }

        var (tz, _) = await GetTzAsync();
        var now = DateTime.UtcNow;

        return tasks
            .OrderByDescending(t => Score(t, now))
            .ThenBy(t => t.CreatedAt)
            .Select(t => new ScheduleCandidateDto
            {
                Id = t.Id,
                Title = t.Title,
                Tags = t.Tags,
                Priority = t.Priority,
                DueDate = UserTime.ToUser(t.DueDate, tz),
                Status = t.Status,
                Steps = t.Steps.OrderBy(s => s.SortOrder).Select(s => new TodoStepDto
                {
                    Id = s.Id,
                    Title = s.Title,
                    IsCompleted = s.IsCompleted,
                    DueDate = UserTime.ToUser(s.DueDate, tz),
                    SortOrder = s.SortOrder,
                }).ToList(),
            })
            .ToList();
    }

    public async Task<TodayScheduleDto?> PickAsync(int entryId, PickScheduleTaskRequest request)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);

        var entry = await _db.ScheduleEntries.Include(e => e.Rule).Include(e => e.Steps)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry is null || entry.Date != today) return null;

        var task = await _db.TodoItems.Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == request.TodoItemId && t.UserId == Uid);
        if (task is null || task.IsArchived
            || task.Status == TodoStatus.Done || task.Status == TodoStatus.Frozen)
        {
            return null;
        }

        var wanted = TagUtil.Split(entry.Rule?.Tags);
        if (wanted.Length > 0 && !TagUtil.ContainsAny(task.Tags, wanted)) return null;

        entry.TodoItemId = task.Id;
        entry.TodoItem = task;
        entry.TitleSnapshot = task.Title;
        entry.IsTaskDeleted = false;
        entry.State = ScheduleEntryState.Scheduled;
        entry.UpdatedAt = DateTime.UtcNow;

        ApplyEntrySteps(entry, task, request.StepIds, emptyMeansAll: true);

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    public async Task<TodayScheduleDto?> SetStepsAsync(int entryId, SetEntryStepsRequest request)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);

        var entry = await _db.ScheduleEntries.Include(e => e.Steps)
            .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry?.TodoItem is not { } task || entry.Date != today) return null;

        ApplyEntrySteps(entry, task, request.StepIds, emptyMeansAll: false);
        entry.UpdatedAt = DateTime.UtcNow;
        RecomputeCompletion(entry, DateTime.UtcNow);

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    /// <summary>
    /// 重设条目今天要做的子步骤。已完成过的步骤始终保留（它们代表已经做掉的工作量）。
    /// <paramref name="emptyMeansAll"/>：挑选任务时空列表表示「全部」；手动改今日步骤时表示「一个都不排」。
    /// </summary>
    private static void ApplyEntrySteps(ScheduleEntry entry, TodoItem task, List<int>? stepIds, bool emptyMeansAll)
    {
        var selectAll = emptyMeansAll && (stepIds is null || stepIds.Count == 0);
        entry.Steps.Clear();

        var order = 0;
        foreach (var step in task.Steps.OrderBy(s => s.SortOrder))
        {
            if (!step.IsCompleted && !selectAll && !(stepIds?.Contains(step.Id) ?? false)) continue;
            entry.Steps.Add(new ScheduleEntryStep
            {
                TodoStepId = step.Id,
                TodoStep = step,
                SortOrder = ++order,
            });
        }
    }

    /// <summary>今天排定的步骤全部完成 → 条目完成；反之退回「已排定」。</summary>
    private static void RecomputeCompletion(ScheduleEntry entry, DateTime now)
    {
        if (entry.Steps.Count > 0 && entry.Steps.All(s => s.TodoStep?.IsCompleted == true))
        {
            entry.State = ScheduleEntryState.Completed;
            entry.CompletedAt ??= now;
        }
        else if (entry.State == ScheduleEntryState.Completed)
        {
            entry.State = ScheduleEntryState.Scheduled;
            entry.CompletedAt = null;
        }
    }

    // ==================== 子步骤推进 ====================

    public async Task<TodayScheduleDto?> ToggleStepAsync(int entryId, int todoStepId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var entry = await _db.ScheduleEntries
            .Include(e => e.Steps).ThenInclude(s => s.TodoStep)
            .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry?.TodoItem is not { } task || entry.Date != today) return null;

        var step = entry.Steps.FirstOrDefault(s => s.TodoStepId == todoStepId)?.TodoStep;
        if (step is null) return null;

        step.IsCompleted = !step.IsCompleted;

        // 与看板一致：勾第一个步骤把任务从「未开始」推进到「进行中」
        if (task.Status == TodoStatus.NotStarted && task.Steps.Any(s => s.IsCompleted))
        {
            task.Status = TodoStatus.InProgress;
        }

        // 今天排定的步骤全部完成 → 日程项完成
        RecomputeCompletion(entry, now);

        // 任务的全部子步骤完成 → 直接进「已完成」（跳过收敛中，不再弹确认）
        if (task.Steps.Count > 0 && task.Steps.All(s => s.IsCompleted))
        {
            task.Status = TodoStatus.Done;
            task.DoneAt ??= now;
        }
        else if (task.Status == TodoStatus.Done)
        {
            task.Status = TodoStatus.InProgress;
            task.DoneAt = null;
        }

        entry.UpdatedAt = now;
        task.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    /// <summary>无子步骤的任务：直接切换该日程项的完成状态。</summary>
    public async Task<TodayScheduleDto?> CompleteEntryAsync(int entryId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var entry = await _db.ScheduleEntries
            .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry is null || entry.Date != today) return null;
        if (entry.TodoItem is { Steps.Count: > 0 }) return null; // 有子步骤请走 ToggleStepAsync

        var done = entry.State != ScheduleEntryState.Completed;
        entry.State = done ? ScheduleEntryState.Completed : ScheduleEntryState.Scheduled;
        entry.CompletedAt = done ? now : null;
        entry.UpdatedAt = now;

        if (entry.TodoItem is { } task)
        {
            if (done)
            {
                task.Status = TodoStatus.Done;
                task.DoneAt ??= now;
            }
            else if (task.Status == TodoStatus.Done)
            {
                task.Status = TodoStatus.InProgress;
                task.DoneAt = null;
            }
            task.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    // ==================== 手动临时安排 ====================

    public async Task<TodayScheduleDto?> CreateManualEntryAsync(CreateManualEntryRequest request)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        if (request.EndTime <= request.StartTime) return null;

        var now = DateTime.UtcNow;
        _db.ScheduleEntries.Add(new ScheduleEntry
        {
            UserId = Uid,
            Date = today,
            RuleId = null,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            State = ScheduleEntryState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    // ==================== 跳过今天 ====================

    public async Task<TodayScheduleDto?> SkipTodayAsync(int ruleId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var rule = await _db.ScheduleRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.UserId == Uid);
        if (rule is null) return null;

        var entry = await _db.ScheduleEntries
            .Include(e => e.TodoItem).ThenInclude(t => t!.Steps)
            .FirstOrDefaultAsync(e => e.UserId == Uid && e.Date == today && e.RuleId == ruleId);

        if (entry is null)
        {
            _db.ScheduleEntries.Add(new ScheduleEntry
            {
                UserId = Uid,
                Date = today,
                RuleId = ruleId,
                StartTime = rule.StartTime,
                EndTime = rule.EndTime,
                State = ScheduleEntryState.Skipped,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            // 生成模式下已建卡且一点没动 → 撤掉这张卡，才真正是「不产生任务」
            if (entry.TodoItem is { } task
                && task.SourceRuleId == ruleId
                && task.Status == TodoStatus.NotStarted
                && task.Steps.All(s => !s.IsCompleted))
            {
                _db.TodoItems.Remove(task);
                entry.TodoItem = null;
                entry.TodoItemId = null;
                entry.TitleSnapshot = null;
                entry.IsTaskDeleted = false;
            }

            entry.State = ScheduleEntryState.Skipped;
            entry.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    // ==================== 昨日未完成的处置 ====================

    public async Task<TodayScheduleDto?> CarryOverAsync(int entryId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var old = await _db.ScheduleEntries.Include(e => e.Steps).ThenInclude(s => s.TodoStep)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (old is null || old.Date >= today) return null;
        if (old.HandledAt is not null) return await BuildTodayAsync(today, tz, tzId);

        ScheduleEntry target;
        var sameRuleToday = old.RuleId is { } rid
            ? await _db.ScheduleEntries.Include(e => e.Steps)
                .FirstOrDefaultAsync(e => e.UserId == Uid && e.Date == today && e.RuleId == rid)
            : null;

        if (sameRuleToday is not null)
        {
            target = sameRuleToday;
        }
        else
        {
            target = new ScheduleEntry
            {
                UserId = Uid,
                Date = today,
                StartTime = old.StartTime,
                EndTime = old.EndTime,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // 借用规则位（若今天该规则的位还空着），这样它在今日视图里仍显示规则名
            if (old.RuleId is { } ruleId)
            {
                var occupied = await _db.ScheduleEntries
                    .AnyAsync(e => e.UserId == Uid && e.Date == today && e.RuleId == ruleId);
                if (!occupied) target.RuleId = ruleId;
            }

            _db.ScheduleEntries.Add(target);
        }

        target.TodoItemId = old.TodoItemId;
        target.TitleSnapshot = old.TitleSnapshot;
        target.IsTaskDeleted = old.IsTaskDeleted;
        target.State = old.TodoItemId is null ? ScheduleEntryState.Pending : ScheduleEntryState.Scheduled;
        target.UpdatedAt = now;

        // 只把还没做完的步骤带过来
        target.Steps.Clear();
        var order = 0;
        foreach (var s in old.Steps.Where(s => s.TodoStep?.IsCompleted != true).OrderBy(s => s.SortOrder))
        {
            target.Steps.Add(new ScheduleEntryStep { TodoStepId = s.TodoStepId, SortOrder = ++order });
        }

        old.HandledAt = now;
        old.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    public async Task<TodayScheduleDto?> FreezeEntryTaskAsync(int entryId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var entry = await _db.ScheduleEntries.Include(e => e.TodoItem)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry is null) return null;

        if (entry.TodoItem is { } task)
        {
            task.PreviousStatus = task.Status;
            task.FrozenReason = "日程未完成";
            task.FrozeAt = now;
            task.Status = TodoStatus.Frozen;
            task.UpdatedAt = now;
        }

        entry.HandledAt = now;
        entry.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    public async Task<TodayScheduleDto?> DeleteEntryTaskAsync(int entryId)
    {
        var (tz, tzId) = await GetTzAsync();
        var today = UserTime.Today(tz);
        var now = DateTime.UtcNow;

        var entry = await _db.ScheduleEntries.Include(e => e.TodoItem)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == Uid);
        if (entry is null) return null;

        if (entry.TodoItem is { } task)
        {
            entry.TitleSnapshot = task.Title;
            entry.IsTaskDeleted = true;
            entry.TodoItem = null;
            entry.TodoItemId = null;
            _db.TodoItems.Remove(task);
        }

        entry.HandledAt = now;
        entry.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return await BuildTodayAsync(today, tz, tzId);
    }

    // ==================== 历史 ====================

    public async Task<List<ScheduleHistoryDto>> GetHistoryAsync(DateOnly from, DateOnly to, int? ruleId = null)
    {
        var (tz, _) = await GetTzAsync();

        var query = EntryQuery().Where(e => e.UserId == Uid && e.Date >= from && e.Date <= to);
        if (ruleId is { } rid) query = query.Where(e => e.RuleId == rid);

        var entries = await query
            .OrderByDescending(e => e.Date).ThenBy(e => e.StartTime)
            .ToListAsync();

        return entries
            .GroupBy(e => e.Date)
            .Select(g => new ScheduleHistoryDto
            {
                Date = g.Key,
                Entries = g.Select(e => ToEntryDto(e, tz)).ToList(),
            })
            .ToList();
    }

    // ==================== 统计 ====================

    public async Task<ScheduleStatsDto> GetStatsAsync()
    {
        var (tz, _) = await GetTzAsync();
        var today = UserTime.Today(tz);

        var start30 = today.AddDays(-29);
        var startPrev30 = today.AddDays(-59);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7; // 周一为一周开始
        var thisMonday = today.AddDays(-daysSinceMonday);
        var flowStart = thisMonday.AddDays(-7 * 7);

        var windowStart = flowStart < startPrev30 ? flowStart : startPrev30;

        var rows = await _db.ScheduleEntries
            .Where(e => e.UserId == Uid && e.Date >= windowStart && e.Date <= today)
            .Select(e => new { e.Date, e.State, e.StartTime, e.EndTime, e.RuleId })
            .ToListAsync();

        static bool IsDone(ScheduleEntryState s) => s == ScheduleEntryState.Completed;
        static bool IsCounted(ScheduleEntryState s) => s != ScheduleEntryState.Skipped;

        var dto = new ScheduleStatsDto();

        var todayRows = rows.Where(r => r.Date == today).ToList();
        dto.TodayDone = todayRows.Count(r => IsDone(r.State));
        dto.TodayPlanned = todayRows.Count(r => IsCounted(r.State));

        var last30 = rows.Where(r => r.Date >= start30).ToList();
        var prev30 = rows.Where(r => r.Date >= startPrev30 && r.Date < start30).ToList();
        dto.Last30Done = last30.Count(r => IsDone(r.State));
        dto.Last30Planned = last30.Count(r => IsCounted(r.State));
        dto.Prev30Done = prev30.Count(r => IsDone(r.State));
        dto.Prev30Planned = prev30.Count(r => IsCounted(r.State));

        // --- 近 8 周排定 / 完成 ---
        for (var i = 7; i >= 0; i--)
        {
            var weekStart = thisMonday.AddDays(-7 * i);
            var weekEnd = weekStart.AddDays(7);
            var weekRows = rows.Where(r => r.Date >= weekStart && r.Date < weekEnd).ToList();
            dto.WeeklyExecution.Add(new WeeklyExecutionDto
            {
                WeekLabel = weekStart.ToString("MM/dd"),
                IsCurrent = i == 0,
                Planned = weekRows.Count(r => IsCounted(r.State)),
                Done = weekRows.Count(r => IsDone(r.State)),
            });
        }

        // --- 日均排期（近30天，按「有安排的天数」平均） ---
        var scheduled = last30.Where(r => IsCounted(r.State)).ToList();
        var scheduledDays = scheduled.Select(r => r.Date).Distinct().Count();
        dto.AvgDailyMinutes = scheduledDays == 0
            ? 0
            : (int)Math.Round(scheduled.Sum(r => (r.EndTime - r.StartTime).TotalMinutes) / scheduledDays);

        // --- 连续全勤：从昨天往前数；有安排且当天全部完成才算一天，没安排的日子跳过不打断 ---
        var byDate = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.ToList());
        var streak = 0;
        for (var d = today.AddDays(-1); d >= today.AddDays(-400); d = d.AddDays(-1))
        {
            if (!byDate.TryGetValue(d, out var dayRows)) continue;
            var counted = dayRows.Where(r => IsCounted(r.State)).ToList();
            if (counted.Count == 0) continue;
            if (counted.All(r => IsDone(r.State))) streak++;
            else break;
        }
        dto.StreakDays = streak;

        // --- 规则执行榜（近30天，只列真的排过的规则） ---
        var ruleIds = last30.Where(r => r.RuleId != null).Select(r => r.RuleId!.Value).Distinct().ToList();
        if (ruleIds.Count > 0)
        {
            var ruleInfos = await _db.ScheduleRules
                .Where(r => r.UserId == Uid && ruleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Title, r.Mode, r.StartTime, r.EndTime })
                .ToListAsync();

            dto.Rules = ruleIds
                .Select(id =>
                {
                    var info = ruleInfos.FirstOrDefault(x => x.Id == id);
                    var group = last30.Where(r => r.RuleId == id).ToList();
                    return new RulePerformanceDto
                    {
                        RuleId = id,
                        Title = info?.Title ?? "已删除的规则",
                        Mode = info?.Mode ?? ScheduleRuleMode.Pick,
                        TimeLabel = info is null ? string.Empty : $"{info.StartTime:HH\\:mm}–{info.EndTime:HH\\:mm}",
                        Planned = group.Count(r => IsCounted(r.State)),
                        Done = group.Count(r => IsDone(r.State)),
                        Skipped = group.Count(r => r.State == ScheduleEntryState.Skipped),
                    };
                })
                .OrderByDescending(r => r.Planned)
                .ThenBy(r => r.Rate)
                .ToList();
        }

        // --- 一条可行动的建议 ---
        var worst = dto.Rules.Where(r => r.Planned >= 5).OrderBy(r => r.Rate).ThenByDescending(r => r.Skipped).FirstOrDefault();
        if (worst is not null && worst.Rate < 80)
        {
            dto.Insight = $"「{worst.Title}」{worst.TimeLabel} 近 30 天排定 {worst.Planned} 次、完成率 {worst.Rate}%"
                + (worst.Skipped > 0 ? $"，其中跳过 {worst.Skipped} 次" : "")
                + " —— 考虑换个时段，或改成挑选模式。";
        }
        else
        {
            var skipHeavy = dto.Rules.Where(r => r.Skipped >= 3).OrderByDescending(r => r.Skipped).FirstOrDefault();
            if (skipHeavy is not null)
            {
                dto.Insight = $"「{skipHeavy.Title}」{skipHeavy.TimeLabel} 近 30 天跳过了 {skipHeavy.Skipped} 次 —— 这个时段可能排得不合适。";
            }
            else if (dto.Last30Planned >= 5 && dto.Last30Rate >= 80)
            {
                dto.Insight = $"近 30 天日程完成率 {dto.Last30Rate}%，排得住，保持。";
            }
        }

        return dto;
    }

    // ==================== 映射与工具 ====================

    private static ScheduleRuleDto ToRuleDto(ScheduleRule r) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Mode = r.Mode,
        DaysOfWeek = r.DaysOfWeek,
        StartTime = r.StartTime,
        EndTime = r.EndTime,
        Tags = r.Tags,
        IsActive = r.IsActive,
        StartDate = r.StartDate,
        EndDate = r.EndDate,
        SortOrder = r.SortOrder,
        Steps = r.Steps.OrderBy(s => s.SortOrder)
            .Select(s => new ScheduleRuleStepDto { Id = s.Id, Title = s.Title, SortOrder = s.SortOrder })
            .ToList(),
    };

    private static ScheduleEntryDto ToEntryDto(ScheduleEntry e, TimeZoneInfo tz) => new()
    {
        Id = e.Id,
        Date = e.Date,
        RuleId = e.RuleId,
        RuleTitle = e.Rule?.Title ?? (e.RuleId is null ? "临时安排" : "已删除的规则"),
        RuleMode = e.Rule?.Mode,
        TodoItemId = e.TodoItemId,
        Title = e.TodoItem?.Title ?? e.TitleSnapshot ?? "（任务已删除）",
        Tags = e.TodoItem?.Tags,
        TodoStatus = e.TodoItem?.Status,
        StartTime = e.StartTime,
        EndTime = e.EndTime,
        State = e.State,
        IsTaskDeleted = e.IsTaskDeleted,
        CompletedAt = UserTime.ToUser(e.CompletedAt, tz),
        Steps = e.Steps.OrderBy(s => s.SortOrder).Select(s => new ScheduleEntryStepDto
        {
            Id = s.Id,
            TodoStepId = s.TodoStepId,
            Title = s.TodoStep?.Title ?? string.Empty,
            IsCompleted = s.TodoStep?.IsCompleted ?? false,
            SortOrder = s.SortOrder,
        }).ToList(),
        TaskSteps = e.TodoItem is null
            ? new List<TodoStepDto>()
            : e.TodoItem.Steps.OrderBy(s => s.SortOrder).Select(s => new TodoStepDto
            {
                Id = s.Id,
                Title = s.Title,
                IsCompleted = s.IsCompleted,
                DueDate = UserTime.ToUser(s.DueDate, tz),
                SortOrder = s.SortOrder,
            }).ToList(),
    };

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
        if (t.DueDate is not { } due) return 0;
        var hours = Math.Max((due - now).TotalHours, 1);
        return Weight(t.Priority) * (1.0 / hours);
    }
}
