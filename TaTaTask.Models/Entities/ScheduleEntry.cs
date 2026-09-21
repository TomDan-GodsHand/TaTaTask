using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Entities;

/// <summary>
/// 日程项：某个日历日（用户时区）的某个时段。
/// 有安排的条目必须指向看板中真实存在的 <see cref="TodoItem"/>（Pending / Skipped 除外）。
/// </summary>
public class ScheduleEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>所属日历日（用户时区）。</summary>
    public DateOnly Date { get; set; }

    /// <summary>来源规则；手动临时添加的条目为空。</summary>
    public int? RuleId { get; set; }

    /// <summary>关联的看板任务。</summary>
    public int? TodoItemId { get; set; }

    /// <summary>任务标题快照：任务被删除后用于历史展示。</summary>
    public string? TitleSnapshot { get; set; }

    /// <summary>关联任务已被硬删除的留痕标记。</summary>
    public bool IsTaskDeleted { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public ScheduleEntryState State { get; set; } = ScheduleEntryState.Pending;

    /// <summary>
    /// 「昨日未完成」里被处置过的时间（顺延 / 冻结 / 删除）。
    /// 非空表示该条不再出现在待处理列表，但历史记录原样保留。
    /// </summary>
    public DateTime? HandledAt { get; set; }

    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ScheduleRule? Rule { get; set; }
    public TodoItem? TodoItem { get; set; }
    public ICollection<ScheduleEntryStep> Steps { get; set; } = new List<ScheduleEntryStep>();
}
