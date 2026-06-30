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

    public User? User { get; set; }
    public ICollection<TodoStep> Steps { get; set; } = new List<TodoStep>();
}
