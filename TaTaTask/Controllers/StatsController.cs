using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaTaTask.Client.Services;
using TaTaTask.Models.Dtos;

namespace TaTaTask.Controllers;

[ApiController]
[Authorize]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly ITodoService _service;

    public StatsController(ITodoService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
        => await _service.GetStatsAsync();
}
