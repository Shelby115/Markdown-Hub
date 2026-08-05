using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;

namespace MarkdownHub.Api.Middleware;

public class RequireAdministratorRequirement : IAuthorizationRequirement { }

/// <summary>
/// Checks the local AppUser.IsAdministrator flag rather than a Keycloak realm role,
/// since app-level admin status is managed inside this application (see
/// CurrentUserService: the first user to ever log in is auto-promoted).
/// </summary>
public class AdministratorAuthorizationHandler : AuthorizationHandler<RequireAdministratorRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdministratorAuthorizationHandler(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RequireAdministratorRequirement requirement)
    {
        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.KeycloakSubjectId == subject);

        if (user is { IsAdministrator: true, IsDisabled: false })
            context.Succeed(requirement);
    }
}
