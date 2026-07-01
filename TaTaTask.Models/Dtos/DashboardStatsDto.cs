namespace TaTaTask.Models.Dtos;

public class DashboardStatsDto
{
    public int ActiveCount { get; set; }
    public int DoneTodayCount { get; set; }
    public int FrozenCount { get; set; }
    public int OverdueCount { get; set; }
    public int TotalStepsActive { get; set; }
    public int CompletedStepsActive { get; set; }
    public List<FrozenItemDto> FrozenItems { get; set; } = new();
    public List<DailyDoneDto> DailyDone { get; set; } = new();
}

public class FrozenItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DaysFrozen { get; set; }
    public string? FrozenReason { get; set; }
}

public class DailyDoneDto
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}
