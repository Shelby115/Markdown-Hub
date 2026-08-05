using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HubPathService _hub;
    private readonly IHttpClientFactory _httpClientFactory;

    public HealthController(AppDbContext db, HubPathService hub, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _hub = hub;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var checks = new Dictionary<string, object>();
        var healthy = true;

        checks["application"] = "running";

        try
        {
            checks["markdownDirectory"] = Directory.Exists(_hub.Root) ? "accessible" : "missing";
            if (!Directory.Exists(_hub.Root)) healthy = false;
        }
        catch (Exception ex)
        {
            checks["markdownDirectory"] = $"error: {ex.Message}";
            healthy = false;
        }

        try
        {
            await _db.Database.CanConnectAsync(ct);
            checks["database"] = "connected";
        }
        catch (Exception ex)
        {
            checks["database"] = $"error: {ex.Message}";
            healthy = false;
        }

        try
        {
            var authority = await _db.OidcProviders.Where(p => p.IsEnabled)
                .OrderBy(p => p.Id).Select(p => p.Authority).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(authority))
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                var resp = await client.GetAsync($"{authority.TrimEnd('/')}/.well-known/openid-configuration", ct);
                checks["oidcProvider"] = resp.IsSuccessStatusCode ? "reachable" : $"unreachable ({(int)resp.StatusCode})";
                if (!resp.IsSuccessStatusCode) healthy = false;
            }
            else
            {
                checks["oidcProvider"] = "not configured";
                healthy = false;
            }
        }
        catch (Exception ex)
        {
            checks["oidcProvider"] = $"unreachable ({ex.GetType().Name})";
            healthy = false;
        }

        var result = new { status = healthy ? "healthy" : "unhealthy", checks };
        return healthy ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
