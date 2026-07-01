using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Client.Services;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Controllers;

[ApiController]
[Authorize]
[Route("api/todos")]
public class TodoController : ControllerBase
{
    private readonly ITodoService _service;

    public TodoController(ITodoService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<TodoItemDto>>> GetBoard([FromQuery] string? q)
        => await _service.GetBoardAsync(q);

    [HttpPost]
    public async Task<ActionResult<TodoItemDto>> Create(CreateTodoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("标题不能为空");
        }

        return await _service.CreateAsync(request);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TodoItemDto>> Update(int id, UpdateTodoRequest request)
    {
        var updated = await _service.UpdateAsync(id, request);
        return updated is null ? NotFound() : updated;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _service.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<TodoItemDto>> ChangeStatus(int id, ChangeStatusRequest request)
    {
        try
        {
            return await _service.ChangeStatusAsync(id, request.Status, request.FrozenReason);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:int}/steps")]
    public async Task<ActionResult<TodoItemDto>> AddStep(int id, AddStepRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("步骤标题不能为空");
        }

        var updated = await _service.AddStepAsync(id, request.Title);
        return updated is null ? NotFound() : updated;
    }

    [HttpPost("{id:int}/steps/{stepId:int}/toggle")]
    public async Task<ActionResult<TodoItemDto>> ToggleStep(int id, int stepId)
    {
        var updated = await _service.ToggleStepAsync(id, stepId);
        return updated is null ? NotFound() : updated;
    }

    [HttpDelete("{id:int}/steps/{stepId:int}")]
    public async Task<ActionResult<TodoItemDto>> DeleteStep(int id, int stepId)
    {
        var updated = await _service.DeleteStepAsync(id, stepId);
        return updated is null ? NotFound() : updated;
    }

    [HttpPost("{id:int}/archive")]
    public async Task<ActionResult<TodoItemDto>> Archive(int id)
    {
        try
        {
            return await _service.ArchiveAsync(id);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
