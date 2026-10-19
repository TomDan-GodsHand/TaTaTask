using TaTaTask.Client.Services;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Models.Enums;
using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>
/// 对外日程接口。全部动作复用 <see cref="IScheduleService"/>，
/// 保持「每日物化」「步骤推进链条」「未完成处置」等既有行为不变。
/// </summary>
public sealed class ScheduleApiService
{
    private readonly IScheduleService _schedule;
    private readonly ApiContext _ctx;
    private readonly ApiCommandExecutor _commands;

    public ScheduleApiService(
        IScheduleService schedule,
        ApiContext ctx,
        ApiCommandExecutor commands)
    {
        _schedule = schedule;
        _ctx = ctx;
        _commands = commands;
    }

    public async Task<TodayScheduleApiDto> GetTodayAsync()
    {
        var tz = await _ctx.GetTimeZoneAsync();
        return ToTodayDto(await _schedule.GetTodayAsync(), tz);
    }

    public async Task<PagedResult<ScheduleRuleApiDto>> ListRulesAsync(int? page, int? pageSize)
    {
        var (p, size) = PageQuery.Normalize(page, pageSize);
        var rules = await _schedule.GetRulesAsync();
        var slice = rules.Skip((p - 1) * size).Take(size).Select(ToRuleDto).ToList();
        return PagedResult<ScheduleRuleApiDto>.Create(slice, p, size, rules.Count);
    }

    public async Task<ScheduleRuleApiDto> GetRuleAsync(int id)
    {
        var rule = (await _schedule.GetRulesAsync()).FirstOrDefault(r => r.Id == id)
            ?? throw ApiException.NotFound("日程规则不存在");
        return ToRuleDto(rule);
    }

    public Task<ScheduleRuleApiDto> CreateRuleAsync(SaveScheduleRuleApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var saved = await _schedule.CreateRuleAsync(ToSaveRequest(request))
                ?? throw ApiException.BadRequest("创建日程规则失败");
            return ToRuleDto(saved);
        });

    public Task<ScheduleRuleApiDto> UpdateRuleAsync(int id, SaveScheduleRuleApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var saved = await _schedule.UpdateRuleAsync(id, ToSaveRequest(request))
                ?? throw ApiException.NotFound("日程规则不存在");
            return ToRuleDto(saved);
        });

    public Task DeleteRuleAsync(int id)
        => _commands.ExecuteAsync(async () =>
        {
            if (!await _schedule.DeleteRuleAsync(id)) throw ApiException.NotFound("日程规则不存在");
        });

    public Task<ScheduleRuleApiDto> PatchRuleAsync(int id, PatchScheduleRuleApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var current = await GetRuleAsync(id);
            var merged = new SaveScheduleRuleApiRequest
            {
                Title = request.Title ?? current.Title,
                Mode = request.Mode ?? current.Mode,
                Days = request.Days,
                DaysOfWeek = request.DaysOfWeek ?? current.DaysOfWeek,
                StartTime = request.StartTime ?? current.StartTime,
                EndTime = request.EndTime ?? current.EndTime,
                Tags = request.Tags ?? current.Tags,
                IsActive = request.IsActive ?? current.IsActive,
                StartDate = request.ClearStartDate ? null : request.StartDate ?? current.StartDate,
                EndDate = request.ClearEndDate ? null : request.EndDate ?? current.EndDate,
                SortOrder = request.SortOrder ?? current.SortOrder,
                Steps = request.Steps ?? current.Steps.Select(s => s.Title).ToList(),
            };
            var saved = await _schedule.UpdateRuleAsync(id, ToSaveRequest(merged))
                ?? throw ApiException.NotFound("日程规则不存在");
            return ToRuleDto(saved);
        });

    public async Task<PagedResult<ScheduleHistoryApiDto>> ListHistoryAsync(
        DateOnly? from, DateOnly? to, int? ruleId, int? page, int? pageSize)
    {
        var tz = await _ctx.GetTimeZoneAsync();
        var today = UserTime.Today(tz);
        var start = from ?? today.AddDays(-6);
        var end = to ?? today;
        if (end < start) throw ApiException.BadRequest("to 不能早于 from");
        if (end.DayNumber - start.DayNumber >= 366) throw ApiException.BadRequest("单次历史查询最多 366 天");

        var (p, size) = PageQuery.Normalize(page, pageSize);
        var days = await _schedule.GetHistoryAsync(start, end, ruleId);
        var slice = days.Skip((p - 1) * size).Take(size)
            .Select(d => new ScheduleHistoryApiDto
            {
                Date = d.Date,
                Total = d.Total,
                Completed = d.Completed,
                Skipped = d.Skipped,
                Entries = d.Entries.Select(e => ToEntryDto(e, tz)).ToList(),
            })
            .ToList();

        return PagedResult<ScheduleHistoryApiDto>.Create(slice, p, size, days.Count);
    }

    public async Task<List<ScheduleCandidateApiDto>> GetCandidatesAsync(int entryId)
    {
        var tz = await _ctx.GetTimeZoneAsync();
        var candidates = await _schedule.GetCandidatesAsync(entryId);
        return candidates.Select(c => new ScheduleCandidateApiDto
        {
            Id = c.Id,
            Title = c.Title,
            Tags = ApiEnums.ParseTags(c.Tags),
            Priority = c.Priority,
            DueDate = ApiTime.WallClockToOffset(c.DueDate, tz),
            Status = ApiEnums.StatusName(c.Status),
            StepCount = c.StepCount,
            DoneStepCount = c.DoneStepCount,
        }).ToList();
    }

    public Task<TodayScheduleApiDto> PickAsync(int entryId, PickTaskApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.PickAsync(entryId, new PickScheduleTaskRequest
            {
                TodoItemId = request.TodoItemId,
                StepIds = request.StepIds ?? new List<int>(),
            }) ?? throw ApiException.BadRequest("无法把该任务排进这个时段");

            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> SetEntryStepsAsync(int entryId, SetEntryStepsApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.SetStepsAsync(entryId, new SetEntryStepsRequest { StepIds = request.StepIds })
                ?? throw ApiException.BadRequest("无法修改今日步骤");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> ToggleEntryStepAsync(int entryId, int stepId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.ToggleStepAsync(entryId, stepId)
                ?? throw ApiException.NotFound("日程项或子步骤不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> CompleteEntryAsync(int entryId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.CompleteEntryAsync(entryId)
                ?? throw ApiException.BadRequest("该日程项不适用直接完成（可能包含子步骤）");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> CreateManualEntryAsync(ManualEntryApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.CreateManualEntryAsync(ToManualRequest(request))
                ?? throw ApiException.BadRequest("结束时间必须晚于开始时间");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> UpdateManualEntryAsync(int entryId, ManualEntryApiRequest request)
        => _commands.ExecuteAsync(async () =>
        {
            var current = (await GetTodayAsync()).Entries.FirstOrDefault(e => e.Id == entryId && e.RuleId is null)
                ?? throw ApiException.NotFound("今天的临时安排不存在");
            var merged = new ManualEntryApiRequest
            {
                StartTime = request.StartTime ?? current.StartTime,
                EndTime = request.EndTime ?? current.EndTime,
                Tags = request.Tags ?? current.Tags,
                Title = request.Title ?? (current.TaskId.HasValue ? current.Title : null),
            };
            var dto = await _schedule.UpdateManualEntryAsync(entryId, ToManualRequest(merged))
                ?? throw ApiException.BadRequest("只能编辑今天的临时安排，且结束时间需晚于开始时间");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> DeleteManualEntryAsync(int entryId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.DeleteManualEntryAsync(entryId)
                ?? throw ApiException.NotFound("日程项不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> SkipTodayAsync(int ruleId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.SkipTodayAsync(ruleId)
                ?? throw ApiException.NotFound("日程规则不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> CarryOverAsync(int entryId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.CarryOverAsync(entryId)
                ?? throw ApiException.NotFound("日程项不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> FreezeEntryTaskAsync(int entryId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.FreezeEntryTaskAsync(entryId)
                ?? throw ApiException.NotFound("日程项不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    public Task<TodayScheduleApiDto> DeleteEntryTaskAsync(int entryId)
        => _commands.ExecuteAsync(async () =>
        {
            var dto = await _schedule.DeleteEntryTaskAsync(entryId)
                ?? throw ApiException.NotFound("日程项不存在");
            return ToTodayDto(dto, await _ctx.GetTimeZoneAsync());
        });

    private static SaveScheduleRuleRequest ToSaveRequest(SaveScheduleRuleApiRequest request)
    {
        if (!ApiEnums.TryParseRuleMode(request.Mode, out var mode))
            throw ApiException.BadRequest("mode 需为 pick 或 generate");

        // days 数组优先；否则用位掩码。两者都没给时视为非法。
        var days = 0;
        if (request.Days is not null)
        {
            if (!ApiEnums.TryParseDays(request.Days, out days))
                throw ApiException.BadRequest("days 需为 mon/tue/wed/thu/fri/sat/sun 的组合");
        }
        else if (request.DaysOfWeek.HasValue)
        {
            days = request.DaysOfWeek.Value;
        }

        if (days is < 1 or > ScheduleDays.All)
            throw ApiException.BadRequest("daysOfWeek 需为 1–127，至少选择一天");

        var start = ApiTime.ParseTime(request.StartTime, new TimeOnly(9, 0));
        var end = ApiTime.ParseTime(request.EndTime, new TimeOnly(10, 0));
        var title = ValidateTitle(request.Title);
        var tags = ValidateTags(request.Tags);
        if (end <= start) throw ApiException.BadRequest("结束时间必须晚于开始时间");
        if (request.EndDate < request.StartDate) throw ApiException.BadRequest("结束日期不能早于开始日期");
        if (mode == ScheduleRuleMode.Pick && string.IsNullOrEmpty(tags))
            throw ApiException.BadRequest("挑选模式必须至少设置一个标签");
        var steps = request.Steps ?? [];
        if (steps.Count > 100) throw ApiException.BadRequest("步骤模板最多 100 个");
        steps = steps.Select(ValidateTitle).ToList();

        return new SaveScheduleRuleRequest
        {
            Title = title,
            Mode = mode,
            DaysOfWeek = days & ScheduleDays.All,
            StartTime = start,
            EndTime = end,
            Tags = tags,
            IsActive = request.IsActive,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SortOrder = request.SortOrder,
            Steps = steps,
        };
    }

    private static CreateManualEntryRequest ToManualRequest(ManualEntryApiRequest request)
    {
        var start = ApiTime.ParseTime(request.StartTime, new TimeOnly(9, 0));
        var end = ApiTime.ParseTime(request.EndTime, new TimeOnly(10, 0));
        return new CreateManualEntryRequest
        {
            StartTime = start,
            EndTime = end,
            Tags = ValidateTags(request.Tags),
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : ValidateTitle(request.Title),
        };
    }

    private static string ValidateTitle(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200) throw ApiException.BadRequest("标题需为 1–200 个字符");
        return value;
    }

    private static string? ValidateTags(string[]? tags)
    {
        var value = ApiEnums.JoinTags(tags);
        if (value is { Length: > 500 }) throw ApiException.BadRequest("标签总长度最多 500 个字符");
        return value;
    }

    private static ScheduleRuleApiDto ToRuleDto(ScheduleRuleDto rule) => new()
    {
        Id = rule.Id,
        Title = rule.Title,
        Mode = ApiEnums.RuleModeName(rule.Mode),
        DaysOfWeek = rule.DaysOfWeek,
        Days = ApiEnums.DayNamesOf(rule.DaysOfWeek),
        StartTime = ApiTime.FormatTime(rule.StartTime),
        EndTime = ApiTime.FormatTime(rule.EndTime),
        Tags = ApiEnums.ParseTags(rule.Tags),
        IsActive = rule.IsActive,
        StartDate = rule.StartDate,
        EndDate = rule.EndDate,
        SortOrder = rule.SortOrder,
        Steps = rule.Steps.Select(s => new ScheduleRuleStepApiDto
        {
            Id = s.Id,
            Title = s.Title,
            SortOrder = s.SortOrder,
        }).ToList(),
    };

    private static TodayScheduleApiDto ToTodayDto(TodayScheduleDto today, TimeZoneInfo tz) => new()
    {
        Date = today.Date,
        TimeZoneId = today.TimeZoneId,
        CurrentEntryId = today.CurrentEntryId,
        NowTime = ApiTime.FormatTime(today.NowTime),
        Entries = today.Entries.Select(e => ToEntryDto(e, tz)).ToList(),
        Unfinished = today.Unfinished.Select(e => ToEntryDto(e, tz)).ToList(),
    };

    private static ScheduleEntryApiDto ToEntryDto(ScheduleEntryDto entry, TimeZoneInfo tz) => new()
    {
        Id = entry.Id,
        Date = entry.Date,
        RuleId = entry.RuleId,
        RuleTitle = entry.RuleTitle,
        RuleMode = entry.RuleMode.HasValue ? ApiEnums.RuleModeName(entry.RuleMode.Value) : null,
        TaskId = entry.TodoItemId,
        Title = entry.Title,
        Tags = ApiEnums.ParseTags(entry.Tags),
        TaskStatus = entry.TodoStatus.HasValue ? ApiEnums.StatusName(entry.TodoStatus.Value) : null,
        StartTime = ApiTime.FormatTime(entry.StartTime),
        EndTime = ApiTime.FormatTime(entry.EndTime),
        State = ApiEnums.EntryStateName(entry.State),
        IsTaskDeleted = entry.IsTaskDeleted,
        TaskCreatedHere = entry.TaskCreatedHere,
        CompletedAt = ApiTime.WallClockToOffset(entry.CompletedAt, tz),
        Steps = entry.Steps.Select(s => new ScheduleEntryStepApiDto
        {
            Id = s.Id,
            TodoStepId = s.TodoStepId,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            SortOrder = s.SortOrder,
        }).ToList(),
        TaskSteps = entry.TaskSteps.Select(s => new TaskStepDto
        {
            Id = s.Id,
            Title = s.Title,
            IsCompleted = s.IsCompleted,
            DueDate = ApiTime.WallClockToOffset(s.DueDate, tz),
            SortOrder = s.SortOrder,
        }).ToList(),
    };

}
