using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Models.Dtos;
using TaTaTask.Services;

namespace TaTaTask.Controllers;

[ApiController]
[Authorize]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IPasswordHasher _hasher;

    public UserController(AppDbContext db, ICurrentUser current, IPasswordHasher hasher)
    {
        _db = db;
        _current = current;
        _hasher = hasher;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings()
    {
        var uid = _current.UserId;
        if (uid is null) return Unauthorized();

        var user = await _db.Users.FindAsync(uid.Value);
        if (user is null) return NotFound();

        return new UserSettingsDto
        {
            Username = user.Username,
            DefaultDueWarningHours = user.DefaultDueWarningHours,
            DefaultDueDays = user.DefaultDueDays,
        };
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(UpdateUserSettingsRequest request)
    {
        var uid = _current.UserId;
        if (uid is null) return Unauthorized();

        var user = await _db.Users.FindAsync(uid.Value);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            if (request.Username != user.Username
                && await _db.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("用户名已被占用");
            }
            user.Username = request.Username;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword)
                || !_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest("当前密码错误");
            }
            user.PasswordHash = _hasher.Hash(request.NewPassword);
        }

        if (request.DefaultDueWarningHours.HasValue)
            user.DefaultDueWarningHours = request.DefaultDueWarningHours.Value;
        if (request.DefaultDueDays.HasValue)
            user.DefaultDueDays = request.DefaultDueDays.Value;

        await _db.SaveChangesAsync();
        return Ok();
    }
}
