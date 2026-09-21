using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Client.Services;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Controllers;

[ApiController]
[Authorize]
[Route("api/schedule")]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleService _service;

    public ScheduleController(IScheduleService service) => _service = service;

    // ---------------- 周规则 ----------------

    [HttpGet("rules")]
    public async Task<ActionResult<List<ScheduleRuleDto>>> GetRules()
        => await _service.GetRulesAsync();

    [HttpPost("rules")]
    public async Task<ActionResult<ScheduleRuleDto>> CreateRule(SaveScheduleRuleRequest request)
    {
        try
        {
            var dto = await _service.CreateRuleAsync(request);
            return dto is null ? BadRequest("创建失败") : dto;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("rules/{id:int}")]
    public async Task<ActionResult<ScheduleRuleDto>> UpdateRule(int id, SaveScheduleRuleRequest request)
    {
        try
        {
            var dto = await _service.UpdateRuleAsync(id, request);
            return dto is null ? NotFound() : dto;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("rules/{id:int}")]
    public async Task<IActionResult> DeleteRule(int id)
        => await _service.DeleteRuleAsync(id) ? NoContent() : NotFound();

    // ---------------- 今日日程 ----------------

    [HttpGet("today")]
    public async Task<ActionResult<TodayScheduleDto>> GetToday()
        => await _service.GetTodayAsync();

    /// <summary>手动加一条临时日程项（无规则）。</summary>
    [HttpPost("entries")]
    public async Task<ActionResult<TodayScheduleDto>> CreateManualEntry(CreateManualEntryRequest request)
    {
        var dto = await _service.CreateManualEntryAsync(request);
        return dto is null ? BadRequest("结束时间必须晚于开始时间") : dto;
    }

    /// <summary>编辑临时安排。</summary>
    [HttpPut("entries/{id:int}")]
    public async Task<ActionResult<TodayScheduleDto>> UpdateManualEntry(int id, CreateManualEntryRequest request)
    {
        var dto = await _service.UpdateManualEntryAsync(id, request);
        return dto is null ? BadRequest("只能编辑今天的临时安排，且结束时间需晚于开始时间") : dto;
    }

    /// <summary>删除临时安排。</summary>
    [HttpDelete("entries/{id:int}")]
    public async Task<ActionResult<TodayScheduleDto>> DeleteManualEntry(int id)
    {
        var dto = await _service.DeleteManualEntryAsync(id);
        return dto is null ? NotFound() : dto;
    }

    [HttpGet("entries/{id:int}/candidates")]
    public async Task<ActionResult<List<ScheduleCandidateDto>>> GetCandidates(int id)
        => await _service.GetCandidatesAsync(id);

    [HttpPost("entries/{id:int}/pick")]
    public async Task<ActionResult<TodayScheduleDto>> Pick(int id, PickScheduleTaskRequest request)
    {
        var dto = await _service.PickAsync(id, request);
        return dto is null ? BadRequest("无法把该任务排入这个时段") : dto;
    }

    [HttpPut("entries/{id:int}/steps")]
    public async Task<ActionResult<TodayScheduleDto>> SetSteps(int id, SetEntryStepsRequest request)
    {
        var dto = await _service.SetStepsAsync(id, request);
        return dto is null ? BadRequest("无法修改今日步骤") : dto;
    }

    [HttpPost("entries/{id:int}/steps/{stepId:int}/toggle")]
    public async Task<ActionResult<TodayScheduleDto>> ToggleStep(int id, int stepId)
    {
        var dto = await _service.ToggleStepAsync(id, stepId);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost("entries/{id:int}/complete")]
    public async Task<ActionResult<TodayScheduleDto>> CompleteEntry(int id)
    {
        var dto = await _service.CompleteEntryAsync(id);
        return dto is null ? BadRequest("该日程项不适用直接完成（可能含有子步骤）") : dto;
    }

    [HttpPost("rules/{id:int}/skip-today")]
    public async Task<ActionResult<TodayScheduleDto>> SkipToday(int id)
    {
        var dto = await _service.SkipTodayAsync(id);
        return dto is null ? NotFound() : dto;
    }

    // ---------------- 昨日未完成的处置 ----------------

    [HttpPost("entries/{id:int}/carry-over")]
    public async Task<ActionResult<TodayScheduleDto>> CarryOver(int id)
    {
        var dto = await _service.CarryOverAsync(id);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost("entries/{id:int}/freeze")]
    public async Task<ActionResult<TodayScheduleDto>> FreezeEntryTask(int id)
    {
        var dto = await _service.FreezeEntryTaskAsync(id);
        return dto is null ? NotFound() : dto;
    }

    [HttpDelete("entries/{id:int}/task")]
    public async Task<ActionResult<TodayScheduleDto>> DeleteEntryTask(int id)
    {
        var dto = await _service.DeleteEntryTaskAsync(id);
        return dto is null ? NotFound() : dto;
    }

    // ---------------- 历史 ----------------

    [HttpGet("history")]
    public async Task<ActionResult<List<ScheduleHistoryDto>>> GetHistory(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? ruleId)
        => await _service.GetHistoryAsync(from, to, ruleId);

    // ---------------- 统计 ----------------

    [HttpGet("stats")]
    public async Task<ActionResult<ScheduleStatsDto>> GetStats()
        => await _service.GetStatsAsync();
}
