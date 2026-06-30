namespace TaTaTask.Models.Entities;

public class TodoStep
{
    public int Id { get; set; }
    public int TodoItemId { get; set; }
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
    public int SortOrder { get; set; }

    public TodoItem? TodoItem { get; set; }
}
