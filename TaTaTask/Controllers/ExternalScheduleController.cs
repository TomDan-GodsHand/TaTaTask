using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.External;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[Microsoft.AspNetCore.Cors.EnableCors("ExternalClients")]
[Authorize(Policy = ExternalAuth.Read)]
[TypeFilter(typeof(ExternalExceptionFilter))]
[Route("api/v1/schedule")]
public class ExternalScheduleController(ExternalApiService service) : ControllerBase
{
    [HttpGet("today")]
    public Task<TodayScheduleDto> Today() => service.TodayAsync();

    [HttpGet("rules")]
    public Task<PagedResult<ScheduleRuleDto>> Rules(int page = 1, int pageSize = 20) => service.RulesAsync(page, pageSize);

    [HttpGet("rules/{id:int}")]
    public Task<ScheduleRuleDto> Rule(int id) => service.RuleAsync(id);

    [HttpPost("rules"), Authorize(Policy = ExternalAuth.Write)]
    public async Task<ActionResult<ScheduleRuleDto>> Create(SaveScheduleRuleRequest request)
    {
        var result = await service.CreateRuleAsync(request);
        return CreatedAtAction(nameof(Rule), new { id = result.Id }, result);
    }

    [HttpPut("rules/{id:int}"), Authorize(Policy = ExternalAuth.Write)]
    public Task<ScheduleRuleDto> Update(int id, SaveScheduleRuleRequest request) => service.UpdateRuleAsync(id, request);

    [HttpDelete("rules/{id:int}"), Authorize(Policy = ExternalAuth.Write)]
    public async Task<IActionResult> Delete(int id) { await service.DeleteRuleAsync(id); return NoContent(); }
}
