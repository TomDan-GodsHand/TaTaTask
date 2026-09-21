namespace TaTaTask.Models.Entities;

/// <summary>生成模式下，每天新建任务卡时复制出来的子步骤模板。</summary>
public class ScheduleRuleStep
{
    public int Id { get; set; }
    public int ScheduleRuleId { get; set; }

    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ScheduleRule? ScheduleRule { get; set; }
}
