using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;

namespace MarkdownHub.Api.Middleware;

public class RequireAdministratorRequirement : IAuthorizationRequirement { }

/// <summary>
/// Checks the local AppUser.IsAdministrator flag - never trusts an external provider's claims
/// directly, since app-level admin status is managed entirely inside this application (see
/// Auth.md §23: external claims must never automatically grant administrative privileges). The
/// "sub" claim on every app-issued JWT is always the local AppUser.Id (see AppTokenService), so
/// this is always a direct row lookup, never a provider-subject match.
/// </summary>
public class AdministratorAuthorizationHandler : AuthorizationHandler<RequireAdministratorRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AdministratorAuthorizationHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RequireAdministratorRequirement requirement)
    {
        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!int.TryParse(subject, out var userId)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(userId);

        if (user is { IsAdministrator: true, IsDisabled: false })
            context.Succeed(requirement);
    }
}
