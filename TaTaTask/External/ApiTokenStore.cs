using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Models.Entities;

namespace TaTaTask.External;

// Only called from the authenticated server-side integrations page, never exposed to bearer tokens.
public sealed class ApiTokenStore(AppDbContext db)
{
    public Task<List<ApiToken>> ListAsync(int userId) => db.ApiTokens.AsNoTracking()
        .Where(t => t.UserId == userId).OrderByDescending(t => t.Id).ToListAsync();

    public async Task<string> CreateAsync(int userId, string name, bool canWrite, int lifetimeDays)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new ArgumentException("名称需为 1–80 个字符");
        if (lifetimeDays is < 1 or > 365)
            throw new ArgumentException("有效期需为 1–365 天");
        if (await db.ApiTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null
                && t.ExpiresAt > DateTime.UtcNow) >= 50)
            throw new ArgumentException("有效令牌最多 50 个，请先撤销不用的令牌");

        var raw = "ttt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        db.ApiTokens.Add(new ApiToken
        {
            UserId = userId, Name = name.Trim(), CanWrite = canWrite,
            TokenHash = ExternalAuth.Hash(raw), Prefix = raw[..12],
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(lifetimeDays),
        });
        await db.SaveChangesAsync();
        return raw;
    }

    public async Task RevokeAsync(int userId, int id)
    {
        await db.ApiTokens.Where(t => t.UserId == userId && t.Id == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
    }
}
