using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Entities;

/// <summary>
/// 周规则：一周中的哪几天、哪个时段、做哪一类任务。
/// 「每天刷新」时按它物化出当天的 <see cref="ScheduleEntry"/>。
/// </summary>
public class ScheduleRule
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public ScheduleRuleMode Mode { get; set; } = ScheduleRuleMode.Pick;

    /// <summary>位掩码：周一 = bit0 … 周日 = bit6（见 <see cref="ScheduleDays"/>）。</summary>
    public int DaysOfWeek { get; set; }

    /// <summary>用户时区下的墙钟时段。</summary>
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>挑选模式：候选任务的标签（逗号分隔，整标签精确匹配）。生成模式：给新建卡打的标签。</summary>
    public string? Tags { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>生效起止（用户时区的日历日）；空表示长期有效。</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<ScheduleRuleStep> Steps { get; set; } = new List<ScheduleRuleStep>();
    public ICollection<ScheduleEntry> Entries { get; set; } = new List<ScheduleEntry>();
}
