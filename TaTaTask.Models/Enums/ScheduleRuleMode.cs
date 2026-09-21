namespace TaTaTask.Models.Enums;

/// <summary>日程规则的取任务方式。</summary>
public enum ScheduleRuleMode
{
    /// <summary>挑选：从该标签的未完成任务里列出候选，由用户选一个。</summary>
    Pick = 1,

    /// <summary>生成：自动新建一张任务卡并排进该时段。</summary>
    Generate = 2,
}
