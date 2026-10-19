using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>
/// 一次外部请求的上下文：当前用户、用户时区、令牌权限。
/// 服务层都从它取用户，保证所有数据访问天然按用户隔离。
/// </summary>
public sealed class ApiContext
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IHttpContextAccessor _accessor;

    private TimeZoneInfo? _tz;
    private string? _tzId;

    public ApiContext(AppDbContext db, ICurrentUser current, IHttpContextAccessor accessor)
    {
        _db = db;
        _current = current;
        _accessor = accessor;
    }

    public int UserId => _accessor.HttpContext?.User.Identity?.AuthenticationType == ApiTokenDefaults.Scheme
        && _current.UserId is int id
            ? id
            : throw new UnauthorizedAccessException("需要有效的访问令牌");

    public bool CanWrite =>
        _accessor.HttpContext?.User.HasClaim(ApiTokenDefaults.ScopeClaim, ApiTokenDefaults.ScopeWrite) == true;

    public string? TokenName =>
        _accessor.HttpContext?.User.FindFirst(ApiTokenDefaults.TokenNameClaim)?.Value;

    public void RequireWrite()
    {
        _ = UserId;
        if (!CanWrite) throw ApiException.Forbidden();
    }

    public async Task<TimeZoneInfo> GetTimeZoneAsync()
    {
        if (_tz is not null) return _tz;
        var id = await _db.Users.AsNoTracking()
            .Where(u => u.Id == UserId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync();
        _tzId = string.IsNullOrWhiteSpace(id) ? UserTime.DefaultTimeZoneId : id;
        _tz = UserTime.Resolve(_tzId);
        return _tz;
    }

    public async Task<string> GetTimeZoneIdAsync()
    {
        await GetTimeZoneAsync();
        return _tzId!;
    }
}
