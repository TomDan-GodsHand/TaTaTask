namespace TaTaTask.Models.Dtos;

public class FeedbackReplyDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
