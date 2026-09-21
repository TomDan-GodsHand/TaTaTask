namespace TaTaTask.Models.Dtos;

public class DashboardStatsDto
{
    // 吞吐回顾（近30天完成口径）
    public int DoneThisWeek { get; set; }
    public int DoneLastWeek { get; set; }
    public int DoneLast30 { get; set; }
    public double? AvgCycleDays { get; set; }
    public int OnTimeDoneCount { get; set; }
    public int OverdueDoneCount { get; set; }
    public double? AvgOverdueDays { get; set; }

    // 周流量（近8周）
    public List<WeeklyFlowDto> WeeklyFlow { get; set; } = new();

    // 当前逾期（活跃任务）
    public int OverdueCount { get; set; }
    public List<OverdueItemDto> OverdueItems { get; set; } = new();

    // 活跃任务分布
    public List<TagCountDto> TagCounts { get; set; } = new();
    public int[] PriorityCounts { get; set; } = new int[5];

    // 老化任务（创建最久未完成）
    public List<AgingItemDto> AgingItems { get; set; } = new();
}

public class WeeklyFlowDto
{
    public string WeekLabel { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int Created { get; set; }
    public int Done { get; set; }
}

public class OverdueItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
}

public class TagCountDto
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AgingItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int AgeDays { get; set; }
}
