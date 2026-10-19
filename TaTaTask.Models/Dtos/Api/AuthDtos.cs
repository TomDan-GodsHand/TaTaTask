namespace TaTaTask.Models.Dtos.Api;

/// <summary>令牌的只读信息（不含明文）。</summary>
public class ApiTokenDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>展示用前缀，例如 <c>tt_AbCdEf12…</c>。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>是否具备写入权限（read 之外的 write 权限）。</summary>
    public bool CanWrite { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>创建令牌的请求。</summary>
public class CreateApiTokenRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>是否允许写入；false 表示只读令牌。</summary>
    public bool CanWrite { get; set; }

    /// <summary>过期时间；为空表示永不过期。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>创建令牌的响应。明文只在这里返回一次。</summary>
public class CreatedApiTokenDto
{
    public ApiTokenDto Token { get; set; } = new();

    /// <summary>明文令牌，形如 <c>tt_xxxxxxxx…</c>。请立即保存，之后无法再次获取。</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>当前令牌对应的用户信息。</summary>
public class MeDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    /// <summary>IANA 时区 ID。所有时间字段都带这个时区的偏移量。</summary>
    public string TimeZoneId { get; set; } = string.Empty;

    public int DefaultDueWarningHours { get; set; }
    public int DefaultDueDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>本次请求使用的令牌名。</summary>
    public string? TokenName { get; set; }

    /// <summary>本次请求使用的令牌是否可写。</summary>
    public bool CanWrite { get; set; }
}
