using MaintenanceAgent.Api.Dtos;
using MaintenanceAgent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceAgent.Api.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly GeminiAgentService _agentService;

    public AgentController(GeminiAgentService agentService)
    {
        _agentService = agentService;
    }

    [HttpPost("process")]
    public async Task<IActionResult> Process(
        [FromBody] AgentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _agentService.ProcessAsync(
                request.PropertyId,
                request.Message,
                request.IdempotencyKey,
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        catch (IdempotencyConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}