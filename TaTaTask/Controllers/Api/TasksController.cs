using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Api;
using TaTaTask.Models.Dtos.Api;

namespace TaTaTask.Controllers.Api;

/// <summary>对外任务接口（/api/v1/tasks）。</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "external")]
[EnableCors("ExternalClients")]
[Authorize(Policy = ApiTokenDefaults.PolicyRead)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Route("api/v1/tasks")]
public sealed class TasksController(TaskApiService tasks) : ControllerBase
{
    /// <summary>分页查询任务。</summary>
    [HttpGet]
    public Task<PagedResult<TaskDto>> List([FromQuery] TaskListQuery query, CancellationToken ct)
        => tasks.ListAsync(query, ct);

    /// <summary>按 Id 取单个任务。</summary>
    [HttpGet("{id:int}")]
    public Task<TaskDto> Get(int id) => tasks.GetAsync(id);

    /// <summary>创建任务。</summary>
    [HttpPost]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public async Task<ActionResult<TaskDto>> Create(CreateTaskRequest request)
    {
        var created = await tasks.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>局部更新任务；只修改出现的字段。</summary>
    [HttpPatch("{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> Patch(int id, PatchTaskRequest request) => tasks.PatchAsync(id, request);

    /// <summary>删除任务（日程项会保留快照）。</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        await tasks.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>状态流转（含冻结/解冻）。</summary>
    [HttpPost("{id:int}/status")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> ChangeStatus(int id, ChangeTaskStatusRequest request)
        => tasks.ChangeStatusAsync(id, request);

    /// <summary>归档（只有已完成任务可以归档）。</summary>
    [HttpPost("{id:int}/archive")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> Archive(int id) => tasks.ArchiveAsync(id);

    [HttpPost("{id:int}/unfreeze")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> Unfreeze(int id) => tasks.UnfreezeAsync(id);

    /// <summary>取消归档。</summary>
    [HttpPost("{id:int}/unarchive")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> Unarchive(int id) => tasks.UnarchiveAsync(id);

    /// <summary>新增子步骤。</summary>
    [HttpPost("{id:int}/steps")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> AddStep(int id, TaskStepRequest request) => tasks.AddStepAsync(id, request.Title);

    /// <summary>重命名子步骤。</summary>
    [HttpPatch("{id:int}/steps/{stepId:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> UpdateStep(int id, int stepId, TaskStepRequest request)
        => tasks.UpdateStepAsync(id, stepId, request.Title);

    /// <summary>删除子步骤。</summary>
    [HttpDelete("{id:int}/steps/{stepId:int}")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> DeleteStep(int id, int stepId) => tasks.DeleteStepAsync(id, stepId);

    /// <summary>切换子步骤完成状态。</summary>
    [HttpPost("{id:int}/steps/{stepId:int}/toggle")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> ToggleStep(int id, int stepId) => tasks.ToggleStepAsync(id, stepId);

    /// <summary>显式设置子步骤完成状态（幂等）。</summary>
    [HttpPut("{id:int}/steps/{stepId:int}/completion")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public Task<TaskDto> SetStepCompletion(int id, int stepId, StepCompletionRequest request)
        => tasks.SetStepCompletionAsync(id, stepId, request.Completed!.Value);

    /// <summary>重排子步骤。</summary>
    [HttpPut("{id:int}/steps/order")]
    [Authorize(Policy = ApiTokenDefaults.PolicyWrite)]
    public async Task<IActionResult> ReorderSteps(int id, ReorderStepsRequest request)
    {
        await tasks.ReorderStepsAsync(id, request.StepIds);
        return NoContent();
    }
}
