using System.ComponentModel.DataAnnotations;

namespace TaTaTask.Models.Entities;

public class FeedbackReply
{
    public int Id { get; set; }
    public int FeedbackItemId { get; set; }
    public int UserId { get; set; }

    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public FeedbackItem? FeedbackItem { get; set; }
    public User? User { get; set; }
}
