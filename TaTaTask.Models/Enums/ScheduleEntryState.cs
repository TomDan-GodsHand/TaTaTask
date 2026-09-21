namespace TaTaTask.Models.Enums;

/// <summary>某个日历日、某个时段上的一条日程项的状态。</summary>
public enum ScheduleEntryState
{
    /// <summary>待选：规则已命中该日，但还没挑定任务（挑选模式）。</summary>
    Pending = 1,

    /// <summary>已排定：已关联到一张看板任务。</summary>
    Scheduled = 2,

    /// <summary>已完成：今日排定的子步骤全部完成。</summary>
    Completed = 3,

    /// <summary>今天跳过：该规则在这一天被显式跳过，不产生任务。</summary>
    Skipped = 4,
}
