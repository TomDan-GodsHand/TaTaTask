using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Client.Services;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Controllers;

[ApiController]
[Authorize]
[Route("api/archive")]
public class ArchiveController : ControllerBase
{
    private readonly ITodoService _service;

    public ArchiveController(ITodoService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<TodoItemDto>>> Get(
        [FromQuery] string? tag,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? q)
        => await _service.GetArchivedAsync(tag, from, to, q);
}
