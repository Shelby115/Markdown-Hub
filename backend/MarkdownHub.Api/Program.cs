using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Middleware;
using MarkdownHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration-driven settings (see appsettings.json "Explicit Settings") ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=/data/db/markdown-hub.db";

Directory.CreateDirectory(Path.GetDirectoryName(connectionString.Split('=', 2).ElementAtOrDefault(1)?.Trim() ?? "/data/db") ?? "/data/db");

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
builder.Services.AddHostedService<HubFileWatcherService>();
builder.Services.AddHostedService<ScheduledBackupHostedService>();
builder.Services.AddHostedService<HistoryCleanupHostedService>();

// --- Auth: OpenID Connect / JWT bearer validation against any enabled OidcProvider ---
// The SPA performs the interactive OIDC login (authorization code + PKCE) directly against
// whichever provider the user picked and attaches the resulting access token to API calls; the
// API's job is purely to validate that bearer token, dynamically, against whichever configured
// provider actually issued it (see Services/OidcProviderValidationService.cs) - there's no
// single fixed Authority anymore since providers are DB-configured and editable at runtime.
builder.Services.AddSingleton<OidcProviderValidationService>();

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
            // surface (browser history, server access logs) as narrow as possible. This is the
            // same "access_token in the query string" pattern ASP.NET Core's own docs recommend
            // for SignalR, for the same underlying reason (no header support on the client side).
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
            OnAuthenticationFailed = async context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                    .LogError(context.Exception, "JWT authentication failed");

                // Not a "failed login" in the traditional sense - this app never sees password
                // attempts at all (the provider's own login page handles those entirely outside
                // this API). This is a rejected/invalid bearer token on an API request, logged as
                // exactly that. Repeated rejections from the same IP in a short window are
                // grouped into one row instead of flooding the log with one per request.
                var audit = context.HttpContext.RequestServices.GetRequiredService<AuditLogService>();
                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
                await audit.LogGroupedAsync("Auth.TokenRejected", null, "Auth", ip,
                    context.Exception.GetType().Name, TimeSpan.FromMinutes(5));
            }
        };
    });

// Wired via DI-aware options configuration (rather than inside an event) so the delegates are
// bound once from OidcProviderValidationService instead of being reassigned on every request.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<OidcProviderValidationService>((options, validation) =>
    {
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) => validation.ValidateIssuer(issuer);
        options.TokenValidationParameters.IssuerSigningKeyResolver =
            (_, securityToken, _, _) => validation.ResolveSigningKeys(securityToken.Issuer);
        options.TokenValidationParameters.AudienceValidator =
            (audiences, securityToken, _) => validation.ValidateAudience(audiences, securityToken.Issuer);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator", policy =>
        policy.Requirements.Add(new RequireAdministratorRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, AdministratorAuthorizationHandler>();

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

// --- Migrate / ensure DB schema + FTS index exist on startup ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var search = scope.ServiceProvider.GetRequiredService<SearchIndexService>();
    await DatabaseMigrations.ApplyAsync(db, search);
    await StartupSeeder.SeedDefaultOidcProviderAsync(db, app.Configuration);
    await StartupSeeder.SeedAdminAsync(db, app.Configuration);
}

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Map("/error", () => Results.Problem("An unexpected error occurred.", statusCode: 500));

app.Run();
