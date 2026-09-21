namespace TaTaTask.Models.Entities;

/// <summary>今日排定的子步骤：日程项 ↔ 看板任务的子步骤，按顺序逐个推进。</summary>
public class ScheduleEntryStep
{
    public int Id { get; set; }
    public int ScheduleEntryId { get; set; }
    public int TodoStepId { get; set; }

    /// <summary>今日推进顺序。</summary>
    public int SortOrder { get; set; }

    public ScheduleEntry? ScheduleEntry { get; set; }
    public TodoStep? TodoStep { get; set; }
}
