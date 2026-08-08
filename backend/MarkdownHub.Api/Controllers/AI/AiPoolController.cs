using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

/// <summary>
/// The one generation-pool action any signed-in user can take: rejecting an entry they were just
/// handed. Not admin-gated on purpose - the person looking at a bad result is the one who knows
/// it's bad, and the worst case is one fewer pre-generated entry, which the generator replaces.
/// </summary>
[ApiController]
[Authorize]
public class AiPoolController : ControllerBase
{
    private readonly GenerationPoolService _pools;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public AiPoolController(GenerationPoolService pools, CurrentUserService currentUser, AuditLogService audit)
    {
        _pools = pools;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Marks a pool entry forgotten - never served again, and never regenerated.</summary>
    [HttpPost("api/ai/pool/entries/{id:int}/forget")]
    public async Task<IActionResult> Forget(int id, CancellationToken ct)
    {
        if (!await _pools.ForgetAsync(id, ct))
        {
            return NotFound(new { message = "That pool entry no longer exists." });
        }

        var user = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(user?.Id, "AiPool.ForgetEntry", id.ToString(), "GenerationPoolEntry", id, ct: ct);
        return NoContent();
    }
}
