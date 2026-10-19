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
[Route("api/v1")]
public class ExternalTasksController(ExternalApiService service) : ControllerBase
{
    [HttpGet("me")]
    public Task<object> Me() => service.MeAsync();

    [HttpGet("tasks")]
    public Task<PagedResult<TodoItemDto>> List([FromQuery] TaskQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);

    [HttpGet("tasks/{id:int}")]
    public Task<TodoItemDto> Get(int id) => service.GetAsync(id);

    [HttpPost("tasks"), Authorize(Policy = ExternalAuth.Write)]
    public async Task<ActionResult<TodoItemDto>> Create(CreateTodoRequest request)
    {
        var result = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("tasks/{id:int}"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> Update(int id, UpdateTodoRequest request) => service.UpdateAsync(id, request);

    [HttpDelete("tasks/{id:int}"), Authorize(Policy = ExternalAuth.Write)]
    public async Task<IActionResult> Delete(int id) { await service.DeleteAsync(id); return NoContent(); }

    [HttpPut("tasks/{id:int}/status"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> Status(int id, ChangeStatusRequest request) => service.ChangeStatusAsync(id, request);

    [HttpPost("tasks/{id:int}/unfreeze"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> Unfreeze(int id) => service.UnfreezeAsync(id);

    [HttpPost("tasks/{id:int}/archive"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> Archive(int id) => service.ArchiveAsync(id);

    [HttpPost("tasks/{id:int}/steps"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> AddStep(int id, AddStepRequest request) => service.AddStepAsync(id, request.Title);

    [HttpPut("tasks/{id:int}/steps/{stepId:int}"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> UpdateStep(int id, int stepId, UpdateStepRequest request)
        => service.UpdateStepAsync(id, stepId, request.Title);

    [HttpDelete("tasks/{id:int}/steps/{stepId:int}"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> DeleteStep(int id, int stepId) => service.DeleteStepAsync(id, stepId);

    [HttpPut("tasks/{id:int}/steps/{stepId:int}/completion"), Authorize(Policy = ExternalAuth.Write)]
    public Task<TodoItemDto> SetStep(int id, int stepId, SetStepCompletionRequest request)
        => service.SetStepAsync(id, stepId, request.Completed!.Value);
}
