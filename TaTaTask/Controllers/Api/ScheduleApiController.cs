using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Api;
using TaTaTask.Models.Dtos.Api;

namespace TaTaTask.Controllers.Api;

/// <summary>对外日程接口（/api/v1/schedule）。</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[EnableCors("ExternalClients")]
[Authorize(Policy = ApiTokenDefaults.PolicyRead)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Route("api/v1/schedule")]
public sealed class ScheduleApiController(ScheduleApiService schedule) : ControllerBase
{
    /// <summary>今日日程（会惰性物化当天的规则条目）。</summary>
    [HttpGet("today")]
    public Task<TodayScheduleApiDto> Today() => schedule.GetTodayAsync();

    /// <summary>分页查询周规则。</summary>
    [HttpGet("rules")]
    public Task<PagedResult<ScheduleRuleApiDto>> ListRules(int? page, int? pageSize)
        => schedule.ListRulesAsync(page, pageSize);

    [HttpGet("rules/{id:int}")]
    public Task<ScheduleRuleApiDto> GetRule(int id) => schedule.GetRuleAsync(id);

    [HttpPost("rules")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public async Task<ActionResult<ScheduleRuleApiDto>> CreateRule(SaveScheduleRuleApiRequest request)
    {
        var created = await schedule.CreateRuleAsync(request);
        return CreatedAtAction(nameof(GetRule), new { id = created.Id }, created);
    }

    [HttpPatch("rules/{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<ScheduleRuleApiDto> UpdateRule(int id, PatchScheduleRuleApiRequest request)
        => schedule.PatchRuleAsync(id, request);

    [HttpPut("rules/{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<ScheduleRuleApiDto> ReplaceRule(int id, SaveScheduleRuleApiRequest request)
        => schedule.UpdateRuleAsync(id, request);

    [HttpDelete("rules/{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public async Task<IActionResult> DeleteRule(int id)
    {
        await schedule.DeleteRuleAsync(id);
        return NoContent();
    }

    /// <summary>跳过某条规则在今天（不产生任务）。</summary>
    [HttpPost("rules/{id:int}/skip-today")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> SkipToday(int id) => schedule.SkipTodayAsync(id);

    /// <summary>按日期区间查日程历史（按天分页）。默认最近 7 天。</summary>
    [HttpGet("history")]
    public Task<PagedResult<ScheduleHistoryApiDto>> History(
        DateOnly? from, DateOnly? to, int? ruleId, int? page, int? pageSize)
        => schedule.ListHistoryAsync(from, to, ruleId, page, pageSize);

    /// <summary>某条日程项的候选任务（挑选模式）。</summary>
    [HttpGet("entries/{id:int}/candidates")]
    public Task<List<ScheduleCandidateApiDto>> Candidates(int id) => schedule.GetCandidatesAsync(id);

    /// <summary>手动加一条临时日程项。</summary>
    [HttpPost("entries")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> CreateManualEntry(ManualEntryApiRequest request)
        => schedule.CreateManualEntryAsync(request);

    /// <summary>编辑一条临时安排。</summary>
    [HttpPatch("entries/{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> UpdateManualEntry(int id, ManualEntryApiRequest request)
        => schedule.UpdateManualEntryAsync(id, request);

    /// <summary>删除一条临时安排。</summary>
    [HttpDelete("entries/{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> DeleteManualEntry(int id) => schedule.DeleteManualEntryAsync(id);

    /// <summary>把候选任务排进该时段。</summary>
    [HttpPost("entries/{id:int}/pick")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> Pick(int id, PickTaskApiRequest request)
        => schedule.PickAsync(id, request);

    /// <summary>改今天排定的子步骤。</summary>
    [HttpPut("entries/{id:int}/steps")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> SetSteps(int id, SetEntryStepsApiRequest request)
        => schedule.SetEntryStepsAsync(id, request);

    /// <summary>勾/取消一个今日步骤，并推进完成链条。</summary>
    [HttpPost("entries/{id:int}/steps/{stepId:int}/toggle")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> ToggleStep(int id, int stepId)
        => schedule.ToggleEntryStepAsync(id, stepId);

    /// <summary>无子步骤的日程项直接完成。</summary>
    [HttpPost("entries/{id:int}/complete")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> Complete(int id) => schedule.CompleteEntryAsync(id);

    /// <summary>把昨日未完成项排进今天。</summary>
    [HttpPost("entries/{id:int}/carry-over")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> CarryOver(int id) => schedule.CarryOverAsync(id);

    /// <summary>冻结昨日未完成项关联的任务。</summary>
    [HttpPost("entries/{id:int}/freeze")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> Freeze(int id) => schedule.FreezeEntryTaskAsync(id);

    /// <summary>删除昨日未完成项关联的任务。</summary>
    [HttpDelete("entries/{id:int}/task")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TodayScheduleApiDto> DeleteEntryTask(int id) => schedule.DeleteEntryTaskAsync(id);
}
