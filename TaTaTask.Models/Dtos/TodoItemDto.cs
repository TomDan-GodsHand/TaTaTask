using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

public class TodoItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string? Tags { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DueWarningHours { get; set; }
    public TodoStatus Status { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    public TodoStatus? PreviousStatus { get; set; }
    public string? FrozenReason { get; set; }
    public DateTime? FrozeAt { get; set; }

    /// <summary>该卡由哪条日程规则自动生成（生成模式）；用于「已完成」泳道按规则折叠分组。</summary>
    public int? SourceRuleId { get; set; }
    public string? SourceRuleTitle { get; set; }

    public List<TodoStepDto> Steps { get; set; } = new();
}

public class TodoStepDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
    public int SortOrder { get; set; }
}
