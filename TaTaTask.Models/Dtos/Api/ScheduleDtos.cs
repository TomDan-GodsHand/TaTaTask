using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos.Api;

/// <summary>对外日程规则视图。</summary>
public class ScheduleRuleApiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>pick（从看板挑任务）或 generate（每天自动建卡）。</summary>
    public string Mode { get; set; } = "pick";

    /// <summary>位掩码：周一 = bit0 … 周日 = bit6。</summary>
    public int DaysOfWeek { get; set; }

    /// <summary>星期缩写数组，例如 <c>["mon","wed","fri"]</c>。</summary>
    public string[] Days { get; set; } = Array.Empty<string>();

    /// <summary>开始时间，<c>HH:mm</c>。</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>结束时间，<c>HH:mm</c>。</summary>
    public string EndTime { get; set; } = string.Empty;

    public string[] Tags { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int SortOrder { get; set; }
    public List<ScheduleRuleStepApiDto> Steps { get; set; } = new();
}

public class ScheduleRuleStepApiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// 创建/更新日程规则。<see cref="Days"/> 与 <see cref="DaysOfWeek"/> 二选一：
/// 提供 <see cref="Days"/> 时优先使用（如 <c>["mon","fri"]</c>）。
/// </summary>
public class SaveScheduleRuleApiRequest
{
    public string Title { get; set; } = string.Empty;
    public string Mode { get; set; } = "pick";
    public int? DaysOfWeek { get; set; }
    public string[]? Days { get; set; }

    /// <summary><c>HH:mm</c>，默认 09:00。</summary>
    public string? StartTime { get; set; }

    /// <summary><c>HH:mm</c>，默认 10:00。</summary>
    public string? EndTime { get; set; }

    public string[]? Tags { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int SortOrder { get; set; }
    public List<string>? Steps { get; set; }
}

/// <summary>局部更新日程规则；未提供的字段保持原值。</summary>
public class PatchScheduleRuleApiRequest
{
    public string? Title { get; set; }
    public string? Mode { get; set; }
    public int? DaysOfWeek { get; set; }
    public string[]? Days { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string[]? Tags { get; set; }
    public bool? IsActive { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool ClearStartDate { get; set; }
    public bool ClearEndDate { get; set; }
    public int? SortOrder { get; set; }
    public List<string>? Steps { get; set; }
}

/// <summary>某天某个时段上的一条日程项。</summary>
public class ScheduleEntryApiDto
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int? RuleId { get; set; }
    public string? RuleTitle { get; set; }
    public string? RuleMode { get; set; }
    public int? TaskId { get; set; }

    /// <summary>任务标题；任务被删除后是当时的快照。</summary>
    public string Title { get; set; } = string.Empty;

    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? TaskStatus { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;

    /// <summary>pending / scheduled / completed / skipped</summary>
    public string State { get; set; } = "pending";

    public bool IsTaskDeleted { get; set; }
    public bool TaskCreatedHere { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ScheduleEntryStepApiDto> Steps { get; set; } = new();

    /// <summary>关联任务的全部子步骤（含今天没排的）。</summary>
    public List<TaskStepDto> TaskSteps { get; set; } = new();
}

public class ScheduleEntryStepApiDto
{
    public int Id { get; set; }
    public int TodoStepId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>今日日程整体。</summary>
public class TodayScheduleApiDto
{
    public DateOnly Date { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public List<ScheduleEntryApiDto> Entries { get; set; } = new();

    /// <summary>今天之前未完成的条目。</summary>
    public List<ScheduleEntryApiDto> Unfinished { get; set; } = new();

    public int? CurrentEntryId { get; set; }

    /// <summary>服务端此刻在用户时区下的墙钟时间，<c>HH:mm</c>。</summary>
    public string NowTime { get; set; } = string.Empty;

    public int PendingCount => Entries.Count(e => e.State == "pending");
}

/// <summary>按天聚合的日程历史。</summary>
public class ScheduleHistoryApiDto
{
    public DateOnly Date { get; set; }
    public List<ScheduleEntryApiDto> Entries { get; set; } = new();
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Skipped { get; set; }
}

/// <summary>手动加一条临时日程项（无规则）。</summary>
public class ManualEntryApiRequest
{
    /// <summary><c>HH:mm</c>，默认 09:00。</summary>
    public string? StartTime { get; set; }

    /// <summary><c>HH:mm</c>，默认 10:00。</summary>
    public string? EndTime { get; set; }

    public string[]? Tags { get; set; }

    /// <summary>填了就自动建一张看板卡并直接排上；不填则留作「待选」。</summary>
    public string? Title { get; set; }
}

/// <summary>把候选任务排进某条日程项。</summary>
public class PickTaskApiRequest
{
    public int TodoItemId { get; set; }

    /// <summary>今天要做的子步骤；空表示全部。</summary>
    public List<int>? StepIds { get; set; }
}

/// <summary>改今天排定的子步骤。</summary>
public class SetEntryStepsApiRequest
{
    public List<int> StepIds { get; set; } = new();
}

/// <summary>挑选模式的候选任务。</summary>
public class ScheduleCandidateApiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public int Priority { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int StepCount { get; set; }
    public int DoneStepCount { get; set; }
}

/// <summary>把状态枚举转成对外字符串用的工具。</summary>
public static class ApiEnums
{
    public static string StatusName(TodoStatus status) => status switch
    {
        TodoStatus.NotStarted => "notStarted",
        TodoStatus.InProgress => "inProgress",
        TodoStatus.Converging => "converging",
        TodoStatus.Done => "done",
        TodoStatus.Frozen => "frozen",
        _ => status.ToString(),
    };

    public static bool TryParseTodoStatus(string? text, out TodoStatus status)
    {
        var normalized = (text ?? string.Empty).Trim();
        foreach (var value in Enum.GetValues<TodoStatus>())
        {
            if (string.Equals(value.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                status = value;
                return true;
            }
        }

        switch (normalized.ToLowerInvariant())
        {
            case "not-started" or "not_started":
                status = TodoStatus.NotStarted;
                return true;
            case "in-progress" or "in_progress" or "doing":
                status = TodoStatus.InProgress;
                return true;
            case "converging" or "finishing":
                status = TodoStatus.Converging;
                return true;
            case "completed" or "finished":
                status = TodoStatus.Done;
                return true;
            case "paused" or "blocked":
                status = TodoStatus.Frozen;
                return true;
            default:
                status = TodoStatus.NotStarted;
                return false;
        }
    }

    public static string RuleModeName(ScheduleRuleMode mode)
        => mode == ScheduleRuleMode.Generate ? "generate" : "pick";

    public static bool TryParseRuleMode(string? text, out ScheduleRuleMode mode)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "pick" or "choose" or "select":
                mode = ScheduleRuleMode.Pick;
                return true;
            case "generate" or "create" or "auto":
                mode = ScheduleRuleMode.Generate;
                return true;
            default:
                mode = ScheduleRuleMode.Pick;
                return false;
        }
    }

    public static string EntryStateName(ScheduleEntryState state) => state switch
    {
        ScheduleEntryState.Pending => "pending",
        ScheduleEntryState.Scheduled => "scheduled",
        ScheduleEntryState.Completed => "completed",
        ScheduleEntryState.Skipped => "skipped",
        _ => state.ToString(),
    };

    /// <summary>星期缩写（mon…sun），下标为 0=mon。</summary>
    public static readonly string[] DayNames = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>把 <c>mon/wed/fri</c> 数组解析为位掩码。</summary>
    public static bool TryParseDays(IEnumerable<string>? days, out int mask)
    {
        mask = 0;
        if (days is null) return false;

        var any = false;
        foreach (var raw in days)
        {
            var name = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            var index = Array.FindIndex(DayNames, d => d == name || d == name[..Math.Min(3, name.Length)]);
            if (index < 0)
            {
                switch (name)
                {
                    case "monday": index = 0; break;
                    case "tuesday": index = 1; break;
                    case "wednesday": index = 2; break;
                    case "thursday": index = 3; break;
                    case "friday": index = 4; break;
                    case "saturday": index = 5; break;
                    case "sunday": index = 6; break;
                    default: return false;
                }
            }

            mask |= 1 << index;
            any = true;
        }

        return any;
    }

    public static string[] DayNamesOf(int mask)
    {
        var result = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            if ((mask & (1 << i)) != 0) result.Add(DayNames[i]);
        }
        return result.ToArray();
    }

    public static string[] ParseTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Array.Empty<string>()
            : tags.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? JoinTags(IEnumerable<string>? tags)
    {
        if (tags is null) return null;
        var parts = tags.Where(t => t is not null).Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    private static bool Set(out TodoStatus target, TodoStatus value)
    {
        target = value;
        return true;
    }
}
