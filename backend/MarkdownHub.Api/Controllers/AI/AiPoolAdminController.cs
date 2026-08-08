using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

/// <summary>
/// Admin management of the generation pools and the background generator that fills them: the
/// per-pool prompt and size limit, and the app-wide pause/window/interval controls.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireAdministrator")]
public class AiPoolAdminController : ControllerBase
{
    private readonly GenerationPoolService _pools;
    private readonly PoolActivityTracker _activity;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public AiPoolAdminController(GenerationPoolService pools, PoolActivityTracker activity, CurrentUserService currentUser, AuditLogService audit)
    {
        _pools = pools;
        _activity = activity;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("api/admin/ai/pools")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var pools = await _pools.ListPoolsAsync(ct);
        var settings = await _pools.GetSettingsAsync(ct);
        var dtos = new List<GenerationPoolDto>();
        foreach (var pool in pools)
        {
            dtos.Add(await ToDtoAsync(pool, settings, ct));
        }
        return Ok(dtos);
    }

    [HttpPost("api/admin/ai/pools")]
    public async Task<IActionResult> Create([FromBody] SaveGenerationPoolRequest request, CancellationToken ct)
    {
        try
        {
            var pool = await _pools.CreatePoolAsync(request.Name, request.Instructions, request.TargetCount, request.Enabled, ct);
            await LogAsync("AiPool.Create", pool, ct);
            return Ok(await ToDtoAsync(pool, await _pools.GetSettingsAsync(ct), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("api/admin/ai/pools/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveGenerationPoolRequest request, CancellationToken ct)
    {
        var pool = await _pools.FindPoolAsync(id, ct);
        if (pool is null)
        {
            return NotFound();
        }

        try
        {
            await _pools.UpdatePoolAsync(pool, request.Instructions, request.TargetCount, request.Enabled, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        await LogAsync("AiPool.Update", pool, ct);
        return Ok(await ToDtoAsync(pool, await _pools.GetSettingsAsync(ct), ct));
    }

    [HttpDelete("api/admin/ai/pools/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var pool = await _pools.FindPoolAsync(id, ct);
        if (pool is null)
        {
            return NotFound();
        }

        await _pools.DeletePoolAsync(pool, ct);
        await LogAsync("AiPool.Delete", pool, ct);
        return NoContent();
    }

    /// <summary>Generates one entry immediately, ignoring the pause/window settings - how an admin
    /// checks whether a prompt edit actually produces what they wanted.</summary>
    [HttpPost("api/admin/ai/pools/{id:int}/generate")]
    public async Task<IActionResult> GenerateOne(int id, CancellationToken ct)
    {
        var pool = await _pools.FindPoolAsync(id, ct);
        if (pool is null)
        {
            return NotFound();
        }

        try
        {
            var entry = await _pools.GenerateEntryAsync(pool, ct);
            return entry is null
                ? BadRequest(new { message = "The model's reply didn't pass this pool's rules, or repeated an entry the pool already has." })
                : Ok(ToDto(entry));
        }
        catch (AiServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    [HttpGet("api/admin/ai/pools/{id:int}/entries")]
    public async Task<IActionResult> Entries(int id, CancellationToken ct)
    {
        if (await _pools.FindPoolAsync(id, ct) is null)
        {
            return NotFound();
        }

        var entries = await _pools.ListEntriesAsync(id, GenerationPoolEntryStatus.Ready, ct);
        return Ok(entries.Select(ToDto).ToList());
    }

    [HttpGet("api/admin/ai/pool-settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct) => Ok(await StatusAsync(ct));

    [HttpPut("api/admin/ai/pool-settings")]
    public async Task<IActionResult> SetSettings([FromBody] GenerationPoolSettingsDto request, CancellationToken ct)
    {
        try
        {
            await _pools.SaveSettingsAsync(new GenerationPoolSettings(
                request.Paused, request.WindowStartUtc, request.WindowEndUtc, request.IntervalSeconds, request.UsedEntryRetentionDays), ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var user = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(user?.Id, "AiPool.Settings", null, "Setting", null,
            $"paused={request.Paused}, window={request.WindowStartUtc ?? "-"}-{request.WindowEndUtc ?? "-"} UTC, interval={request.IntervalSeconds}s", ct: ct);

        return Ok(await StatusAsync(ct));
    }

    private async Task<GenerationPoolStatusDto> StatusAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = await _pools.GetSettingsAsync(ct);
        return new GenerationPoolStatusDto(
            new GenerationPoolSettingsDto(settings.Paused, settings.WindowStartUtc, settings.WindowEndUtc,
                settings.IntervalSeconds, settings.UsedEntryRetentionDays),
            settings.IsAllowedAt(now),
            ReasonFor(settings, now),
            _activity.CurrentPoolName,
            SecondsUntilNextCheck(now),
            now.UtcDateTime.ToString("HH:mm"));
    }

    /// <summary>Clamped to the configured interval: a pass that overran (a slow model call) would
    /// otherwise report a due time in the past, which a countdown can't do anything sensible with.</summary>
    private int? SecondsUntilNextCheck(DateTimeOffset now)
    {
        if (_activity.NextPassDueUtc is not DateTimeOffset due)
        {
            return null;
        }
        return Math.Max(0, (int)Math.Ceiling((due - now).TotalSeconds));
    }

    private static string ReasonFor(GenerationPoolSettings settings, DateTimeOffset now)
    {
        if (settings.Paused)
        {
            return "Paused for every pool - nothing will be generated until you resume.";
        }
        if (!settings.IsWithinWindow(now))
        {
            return $"Generation is only allowed between {settings.WindowDescription}, and it is now {now.UtcDateTime:HH:mm} UTC.";
        }
        return settings.WindowDescription is string window
            ? $"Inside the allowed window ({window}), topping up whichever pool needs it most."
            : "No window set, so pools are topped up at any hour.";
    }

    private async Task<GenerationPoolDto> ToDtoAsync(GenerationPool pool, GenerationPoolSettings settings, CancellationToken ct)
    {
        var ready = await _pools.CountReadyAsync(pool.Id, ct);
        var status = PoolFillStatus.For(pool, ready, settings, DateTimeOffset.UtcNow, _activity.CurrentPoolName == pool.Name);
        return new(pool.Id, pool.Name, pool.Instructions, pool.TargetCount, pool.Enabled, ready,
            status.Label, status.Reason, pool.UpdatedAtUtc.UtcDateTime.ToString("o"));
    }

    private static GenerationPoolEntryDto ToDto(GenerationPoolEntry entry) =>
        new(entry.Id, entry.Content, entry.Status, entry.CreatedAtUtc.UtcDateTime.ToString("o"));

    private async Task LogAsync(string action, GenerationPool pool, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(user?.Id, action, pool.Name, "GenerationPool", pool.Id, ct: ct);
    }
}
