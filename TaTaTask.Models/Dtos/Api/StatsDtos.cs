namespace TaTaTask.Models.Dtos.Api;

/// <summary>看板与日程的汇总统计。</summary>
public class StatsApiDto
{
    public int ActiveCount { get; set; }
    public int OverdueCount { get; set; }
    public int DoneThisWeek { get; set; }
    public int DoneLastWeek { get; set; }
    public int DoneLast30 { get; set; }
    public double? AvgCycleDays { get; set; }
    public int OnTimeDoneCount { get; set; }
    public int OverdueDoneCount { get; set; }
    public double? AvgOverdueDays { get; set; }

    public List<WeeklyFlowApiDto> WeeklyFlow { get; set; } = new();
    public List<TagCountApiDto> TagCounts { get; set; } = new();
    public int[] PriorityCounts { get; set; } = new int[5];
    public List<OverdueTaskApiDto> OverdueTasks { get; set; } = new();
    public List<AgingTaskApiDto> AgingTasks { get; set; } = new();

    /// <summary>日程统计摘要；没有日程数据时为 null。</summary>
    public ScheduleSummaryApiDto? Schedule { get; set; }
}

public class WeeklyFlowApiDto
{
    public string WeekLabel { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int Created { get; set; }
    public int Done { get; set; }
}

public class TagCountApiDto
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class OverdueTaskApiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public int DaysOverdue { get; set; }
}

public class AgingTaskApiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int AgeDays { get; set; }
}

public class ScheduleSummaryApiDto
{
    public int TodayDone { get; set; }
    public int TodayPlanned { get; set; }
    public int Last30Done { get; set; }
    public int Last30Planned { get; set; }
    public int Last30Rate { get; set; }
    public int StreakDays { get; set; }
    public int AvgDailyMinutes { get; set; }
}
