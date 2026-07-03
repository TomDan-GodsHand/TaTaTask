using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Entities;
using TaTaTask.Services;

namespace TaTaTask.Controllers;

[ApiController]
[Route("api/feedback")]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;

    public FeedbackController(AppDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet]
    public async Task<ActionResult<List<FeedbackItemDto>>> GetAll()
    {
        var items = await _db.FeedbackItems
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FeedbackItemDto
            {
                Id = f.Id,
                Title = f.Title,
                Content = f.Content,
                Username = f.User != null ? f.User.Username : "未知用户",
                CreatedAt = f.CreatedAt,
                Replies = f.Replies.OrderBy(r => r.CreatedAt).Select(r => new FeedbackReplyDto
                {
                    Id = r.Id,
                    Content = r.Content,
                    Username = r.User != null ? r.User.Username : "未知用户",
                    CreatedAt = r.CreatedAt,
                }).ToList(),
            })
            .ToListAsync();

        return items;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<FeedbackItemDto>> Create(FeedbackRequest request)
    {
        if (!_current.IsAuthenticated || _current.UserId is null)
            return Unauthorized();

        var item = new FeedbackItem
        {
            UserId = _current.UserId.Value,
            Title = request.Title,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow,
        };

        _db.FeedbackItems.Add(item);
        await _db.SaveChangesAsync();

        return new FeedbackItemDto
        {
            Id = item.Id,
            Title = item.Title,
            Content = item.Content,
            Username = _current.Username ?? "未知用户",
            CreatedAt = item.CreatedAt,
        };
    }

    [HttpPost("{id:int}/reply")]
    [Authorize]
    public async Task<ActionResult<FeedbackReplyDto>> Reply(int id, FeedbackReplyRequest request)
    {
        if (!_current.IsAuthenticated || _current.UserId is null)
            return Unauthorized();

        if (!_current.IsAdmin)
            return Forbid();

        var feedback = await _db.FeedbackItems.FindAsync(id);
        if (feedback is null)
            return NotFound();

        var reply = new FeedbackReply
        {
            FeedbackItemId = id,
            UserId = _current.UserId.Value,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow,
        };

        _db.FeedbackReplies.Add(reply);
        await _db.SaveChangesAsync();

        return new FeedbackReplyDto
        {
            Id = reply.Id,
            Content = reply.Content,
            Username = _current.Username ?? "未知用户",
            CreatedAt = reply.CreatedAt,
        };
    }
}
