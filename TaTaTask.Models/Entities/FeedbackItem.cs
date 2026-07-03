using System.ComponentModel.DataAnnotations;

namespace TaTaTask.Models.Entities;

public class FeedbackItem
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<FeedbackReply> Replies { get; set; } = new List<FeedbackReply>();
}
