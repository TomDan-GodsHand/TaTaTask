using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Entities;

public class TodoItem
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string? Tags { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DueWarningHours { get; set; }
    public TodoStatus Status { get; set; } = TodoStatus.NotStarted;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    public TodoStatus? PreviousStatus { get; set; }
    public string? FrozenReason { get; set; }
    public DateTime? FrozeAt { get; set; }

    /// <summary>该卡由哪条日程规则自动生成（生成模式）；用于「已完成」泳道按规则折叠分组。</summary>
    public int? SourceRuleId { get; set; }

    public User? User { get; set; }
    public ScheduleRule? SourceRule { get; set; }
    public ICollection<TodoStep> Steps { get; set; } = new List<TodoStep>();
}
