using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

/// <summary>周规则：一周中的哪几天、哪个时段、做哪一类任务。</summary>
public class ScheduleRuleDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ScheduleRuleMode Mode { get; set; }

    /// <summary>位掩码：周一 = bit0 … 周日 = bit6。</summary>
    public int DaysOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>挑选模式：候选标签；生成模式：给新卡打的标签。逗号分隔，整标签精确匹配。</summary>
    public string? Tags { get; set; }

    public bool IsActive { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int SortOrder { get; set; }

    /// <summary>生成模式的步骤模板。</summary>
    public List<ScheduleRuleStepDto> Steps { get; set; } = new();

    public string DaysLabel => ScheduleDays.Label(DaysOfWeek);
    public string TimeLabel => $"{StartTime:HH\\:mm}–{EndTime:HH\\:mm}";
}

public class ScheduleRuleStepDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class SaveScheduleRuleRequest
{
    public string Title { get; set; } = string.Empty;
    public ScheduleRuleMode Mode { get; set; } = ScheduleRuleMode.Pick;
    public int DaysOfWeek { get; set; }
    public TimeOnly StartTime { get; set; } = new(9, 0);
    public TimeOnly EndTime { get; set; } = new(10, 0);
    public string? Tags { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int SortOrder { get; set; }

    /// <summary>生成模式的步骤模板（按顺序）。挑选模式忽略。</summary>
    public List<string> Steps { get; set; } = new();
}
