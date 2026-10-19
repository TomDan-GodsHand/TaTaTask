using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Models.Entities;

namespace TaTaTask.Api;

/// <summary>
/// 访问令牌的签发、校验与吊销。数据库只保存 SHA-256 摘要；明文只在创建时返回一次。
/// </summary>
public sealed class ApiTokenService
{
    /// <summary>每个账号最多保留的有效令牌数。</summary>
    public const int MaxActiveTokens = 50;

    private readonly AppDbContext _db;

    public ApiTokenService(AppDbContext db) => _db = db;

    public static string Hash(string plainText)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));

    public async Task<CreatedApiTokenDto> CreateAsync(int userId, CreateApiTokenRequest request, TimeZoneInfo tz)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80)
            throw ApiException.BadRequest("令牌名称需为 1–80 个字符");

        var now = DateTime.UtcNow;
        var expiresAt = request.ExpiresAt?.UtcDateTime;
        if (expiresAt.HasValue && expiresAt.Value <= now)
            throw ApiException.BadRequest("过期时间必须晚于当前时间");

        var active = await _db.ApiTokens.CountAsync(t => t.UserId == userId
            && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now));
        if (active >= MaxActiveTokens)
            throw ApiException.Conflict($"最多保留 {MaxActiveTokens} 个有效令牌，请先吊销不用的令牌");

        var secret = ApiTokenDefaults.TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new ApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = Hash(secret),
            Prefix = secret[..12],
            CanWrite = request.CanWrite,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        _db.ApiTokens.Add(token);
        await _db.SaveChangesAsync();

        return new CreatedApiTokenDto
        {
            Secret = secret,
            Token = ToDto(token, tz),
        };
    }

    public async Task<List<ApiTokenDto>> ListAsync(int userId, TimeZoneInfo tz)
    {
        var tokens = await _db.ApiTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Id)
            .ToListAsync();
        return tokens.Select(t => ToDto(t, tz)).ToList();
    }

    public async Task<bool> RevokeAsync(int userId, int id)
    {
        var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (token is null) return false;

        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return true;
    }

    /// <summary>校验明文令牌；成功时返回带权限声明的身份。</summary>
    public async Task<ClaimsPrincipal?> AuthenticateAsync(string plainText, CancellationToken ct)
    {
        if (!plainText.StartsWith(ApiTokenDefaults.TokenPrefix, StringComparison.Ordinal)
            && !plainText.StartsWith("tt_", StringComparison.Ordinal))
            return null;

        var hash = Hash(plainText);
        var token = await _db.ApiTokens.Include(t => t.User).AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token?.User is null || token.RevokedAt is not null)
            return null;

        var now = DateTime.UtcNow;
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value <= now)
            return null;

        // 最后使用时间不需要每次请求都写库，最多每分钟更新一次。
        if (token.LastUsedAt is null || token.LastUsedAt < now.AddMinutes(-1))
        {
            await _db.ApiTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), ct);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.UserId.ToString()),
            new(ClaimTypes.Name, token.User.Username),
            new(ApiTokenDefaults.ScopeClaim, ApiTokenDefaults.ScopeRead),
            new(ApiTokenDefaults.TokenNameClaim, token.Name),
        };

        if (token.CanWrite)
            claims.Add(new Claim(ApiTokenDefaults.ScopeClaim, ApiTokenDefaults.ScopeWrite));

        var identity = new ClaimsIdentity(claims, ApiTokenDefaults.Scheme);
        return new ClaimsPrincipal(identity);
    }

    private static ApiTokenDto ToDto(ApiToken token, TimeZoneInfo tz) => new()
    {
        Id = token.Id,
        Name = token.Name,
        Prefix = token.Prefix,
        CanWrite = token.CanWrite,
        CreatedAt = ApiTime.ToOffset(token.CreatedAt, tz),
        LastUsedAt = ApiTime.ToOffset(token.LastUsedAt, tz),
        ExpiresAt = ApiTime.ToOffset(token.ExpiresAt, tz),
        RevokedAt = ApiTime.ToOffset(token.RevokedAt, tz),
        IsActive = token.RevokedAt is null
            && (token.ExpiresAt is null || token.ExpiresAt > DateTime.UtcNow),
    };

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
