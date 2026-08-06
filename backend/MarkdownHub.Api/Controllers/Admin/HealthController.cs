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

        // Local username/password login never depends on an external provider (Auth.md §9/§31.10),
        // so an absent or unreachable provider is informational only - it must never flip the
        // overall health check to unhealthy, or a broken/unconfigured IdP would take down an
        // otherwise fully-working, locally-authenticatable deployment.
        try
        {
            var configJson = await _db.AuthenticationProviders
                .Where(p => p.Enabled && p.Type == Data.Entities.AuthProviderType.Oidc)
                .OrderBy(p => p.Id).Select(p => p.ConfigurationJson).FirstOrDefaultAsync(ct);
            var authority = configJson is null ? null : Services.ExternalAuthService.ParseConfiguration(configJson).Authority;

            if (!string.IsNullOrEmpty(authority))
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                var resp = await client.GetAsync($"{authority.TrimEnd('/')}/.well-known/openid-configuration", ct);
                checks["externalOidcProvider"] = resp.IsSuccessStatusCode ? "reachable" : $"unreachable ({(int)resp.StatusCode})";
            }
            else
            {
                checks["externalOidcProvider"] = "not configured (local login is available)";
            }
        }
        catch (Exception ex)
        {
            checks["externalOidcProvider"] = $"unreachable ({ex.GetType().Name})";
        }

        var result = new { status = healthy ? "healthy" : "unhealthy", checks };
        return healthy ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
