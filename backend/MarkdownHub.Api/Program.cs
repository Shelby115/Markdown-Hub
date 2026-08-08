using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Middleware;
using MarkdownHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration-driven settings (see appsettings.json "Explicit Settings") ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=/data/db/markdown-hub.db";

Directory.CreateDirectory(Path.GetDirectoryName(connectionString.Split('=', 2).ElementAtOrDefault(1)?.Trim() ?? "/data/db") ?? "/data/db");

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/keys";
Directory.CreateDirectory(dataProtectionKeysPath);

// --- Database ---
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(connectionString));

// --- Core services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<HubPathService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<MarkdownFileService>();
builder.Services.AddSingleton<MarkdownRenderService>();
builder.Services.AddScoped<SearchIndexService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<VersionService>();
builder.Services.AddScoped<HistorySettingsService>();
builder.Services.AddScoped<IAiService, OllamaAiService>();
builder.Services.AddScoped<AiTemplateService>();
builder.Services.AddHostedService<HubFileWatcherService>();
builder.Services.AddHostedService<ScheduledBackupHostedService>();
builder.Services.AddHostedService<HistoryCleanupHostedService>();

// --- Auth: local username/password is the foundation; OIDC/OAuth2 providers are optional
// linked identities the server authenticates through on the user's behalf (see Auth.md). The
// app is always the JWT issuer/signer - external provider tokens are exchanged server-side in
// AuthController and never handed to the browser.
builder.Services.AddDataProtection()
    .SetApplicationName("markdown-hub")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<JwtSigningKeyProvider>();
builder.Services.AddSingleton<ProviderSecretProtector>();
builder.Services.AddScoped<AppTokenService>();
builder.Services.AddScoped<AccountSafetyService>();
builder.Services.AddScoped<ExternalAuthService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.Events = new JwtBearerEvents
        {
            // <audio>/<video>/<iframe> src attributes can't attach an Authorization header, so
            // the live editor's media embeds (Services/MarkdownRenderService.cs, liveMarkdown.ts)
            // pass the access token as a query param instead for this one route - scoped to
            // exactly "/api/attachments" (never the whole API) to keep the token's extra exposure
            // surface (browser history, server access logs) as narrow as possible.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/api/attachments") &&
                    context.Request.Query.TryGetValue("access_token", out var queryToken))
                {
                    context.Token = queryToken;
                }
                return Task.CompletedTask;
            },
            // Every app-issued token carries a "sid" claim tied to a Session row - checking it
            // here (rather than trusting the token's own unexpired signature alone) is what
            // makes an otherwise-stateless bearer JWT individually revocable: a user or admin
            // revoking a session, or a password change invalidating other sessions, takes effect
            // immediately instead of waiting out the token's remaining lifetime.
            OnTokenValidated = async context =>
            {
                var sidClaim = context.Principal?.FindFirstValue("sid");
                if (!Guid.TryParse(sidClaim, out var sessionId))
                {
                    context.Fail("Token is missing a valid session id.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var session = await db.Sessions.FindAsync(sessionId);
                if (session is null || !session.IsActive)
                {
                    context.Fail("Session has been revoked or has expired.");
                    return;
                }

                if (DateTimeOffset.UtcNow - session.LastActivityAt > TimeSpan.FromMinutes(1))
                {
                    session.LastActivityAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            },
            OnAuthenticationFailed = async context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                    .LogWarning(context.Exception, "JWT authentication failed");

                // Repeated rejections from the same IP in a short window are grouped into one
                // row instead of flooding the log with one per request.
                var audit = context.HttpContext.RequestServices.GetRequiredService<AuditLogService>();
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
                await audit.LogGroupedAsync("Auth.TokenRejected", null, "Auth", ip,
                    context.Exception.GetType().Name, TimeSpan.FromMinutes(5));
            }
        };
    });

// Wired via DI-aware options configuration (rather than inside an event) so the delegate is
// bound once JwtSigningKeyProvider has been populated (see the startup block below) instead of
// being reassigned on every request - same idiom the old multi-issuer validation service used.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSigningKeyProvider>((options, keyProvider) =>
    {
        options.TokenValidationParameters.ValidIssuer = AppTokenService.Issuer;
        options.TokenValidationParameters.ValidAudience = AppTokenService.Audience;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => [keyProvider.GetKey()];
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator", policy =>
        policy.Requirements.Add(new RequireAdministratorRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, AdministratorAuthorizationHandler>();

// Local login is rate-limited per source IP to slow down password guessing (Auth.md §21) -
// deliberately a short queue-less fixed window (reject immediately over the limit) rather than
// a permanent lockout, which could itself become a denial-of-service vector.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0,
        }));
});

// --- CORS for the SPA dev server / separately-hosted frontend ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- Migrate / ensure DB schema + FTS index exist, seed the admin account, and resolve the JWT
// signing key on startup ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var search = scope.ServiceProvider.GetRequiredService<SearchIndexService>();
    await DatabaseMigrations.ApplyAsync(db, search);

    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
    await StartupSeeder.SeedAdminAsync(db, app.Configuration, passwordHasher);

    var secretProtector = scope.ServiceProvider.GetRequiredService<ProviderSecretProtector>();
    await StartupSeeder.SeedDefaultProviderAsync(db, app.Configuration, secretProtector);

    // Resolved once here (rather than per-request) since there is now exactly one fixed
    // self-issued signing key, unlike the old per-provider dynamic key resolution.
    var tokenService = scope.ServiceProvider.GetRequiredService<AppTokenService>();
    var signingKey = await tokenService.GetSigningKeyAsync();
    app.Services.GetRequiredService<JwtSigningKeyProvider>().SetKey(signingKey);
}

// Must run before anything that reads Request.Scheme/Request.Host (HTTPS redirection, and
// AuthController's OIDC/OAuth2 callback redirect_uri construction) - the frontend's nginx proxies
// /api to this container over plain HTTP, so without trusting its X-Forwarded-* headers the app
// has no way to know the original request was actually HTTPS. KnownNetworks/KnownProxies are
// cleared because the proxy is a same-Docker-network container, not a fixed known IP.
var forwardedHeadersOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (app.Environment.IsDevelopment())
{
    // PII (claim values like emails/subject IDs) in JWT validation logs is useful for local
    // debugging but must never be enabled outside Development - it would otherwise write
    // personal data from every request's token into production logs in plaintext.
    Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // No Markdown filesystem path should ever be directly exposed to the browser;
    // the default exception page can leak stack traces/paths, so use a generic handler in prod.
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Map("/error", () => Results.Problem("An unexpected error occurred.", statusCode: 500));

app.Run();
