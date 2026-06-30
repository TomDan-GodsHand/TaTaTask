namespace TaTaTask.Models.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int DefaultDueWarningHours { get; set; } = 48;
    public int DefaultDueDays { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TodoItem> TodoItems { get; set; } = new List<TodoItem>();
}
