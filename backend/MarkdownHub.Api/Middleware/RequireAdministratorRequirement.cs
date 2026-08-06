using Microsoft.AspNetCore.Authorization;

namespace MarkdownHub.Api.Middleware;

public class RequireAdministratorRequirement : IAuthorizationRequirement { }
