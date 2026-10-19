using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Api;
using TaTaTask.Models.Dtos.Api;

namespace TaTaTask.Controllers.Api;

/// <summary>当前令牌对应的用户信息。</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[EnableCors("ExternalClients")]
[Authorize(Policy = ApiTokenDefaults.PolicyRead)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Route("api/v1/me")]
public sealed class MeController(UserApiService users) : ControllerBase
{
    [HttpGet]
    public Task<MeDto> Get() => users.GetMeAsync();
}

/// <summary>标签池与计数。</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[EnableCors("ExternalClients")]
[Authorize(Policy = ApiTokenDefaults.PolicyRead)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Route("api/v1/tags")]
public sealed class TagsController(UserApiService users) : ControllerBase
{
    /// <summary>按使用次数倒序列出标签；默认只统计未归档任务。</summary>
    [HttpGet]
    public Task<List<TagCountApiDto>> Get([FromQuery] bool includeArchived = false)
        => users.GetTagsAsync(includeArchived);
}

/// <summary>看板与日程的汇总统计。</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[EnableCors("ExternalClients")]
[Authorize(Policy = ApiTokenDefaults.PolicyRead)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Route("api/v1/stats")]
public sealed class StatsApiController(UserApiService users) : ControllerBase
{
    [HttpGet]
    public Task<StatsApiDto> Get() => users.GetStatsAsync();
}
