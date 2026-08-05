using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Validates bearer tokens against whichever enabled OidcProvider issued them, instead of the
/// single fixed Authority JwtBearer normally assumes. Providers live in the DB and are editable
/// via the admin UI at runtime, so this can't use ASP.NET's usual "one static AddJwtBearer per
/// known issuer" approach - it plugs into TokenValidationParameters' IssuerValidator /
/// IssuerSigningKeyResolver / AudienceValidator instead (see Program.cs), which are synchronous
/// delegates called from inside JwtSecurityTokenHandler.ValidateToken. Blocking on the async
/// DB/JWKS calls here is the standard, documented shape for multi-tenant JWT validation in
/// ASP.NET Core - both the provider list and each provider's JWKS are cached (60s and the
/// ConfigurationManager's own ~24h default respectively), so the blocking call almost always
/// resolves from memory rather than actually waiting on I/O.
/// </summary>
public class OidcProviderValidationService
{
    private static readonly TimeSpan ProviderCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _configManagers = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<OidcProvider> _cachedProviders = [];
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public OidcProviderValidationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IEnumerable<SecurityKey> ResolveSigningKeys(string issuer)
    {
        var provider = FindProviderByIssuerAsync(issuer).GetAwaiter().GetResult();
        if (provider is null) return [];
        var config = GetConfigManager(provider).GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
        return config.SigningKeys;
    }

    public string ValidateIssuer(string issuer)
    {
        var provider = FindProviderByIssuerAsync(issuer).GetAwaiter().GetResult();
        if (provider is null)
            throw new SecurityTokenInvalidIssuerException($"'{issuer}' does not match any enabled OIDC provider.");
        return issuer;
    }

    public bool ValidateAudience(IEnumerable<string> audiences, string issuer)
    {
        var provider = FindProviderByIssuerAsync(issuer).GetAwaiter().GetResult();
        return provider is not null && audiences.Contains(provider.Audience, StringComparer.Ordinal);
    }

    private async Task<OidcProvider?> FindProviderByIssuerAsync(string issuer)
    {
        var providers = await GetEnabledProvidersAsync();
        return providers.FirstOrDefault(p => string.Equals(
            p.Authority.TrimEnd('/'), issuer.TrimEnd('/'), StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<OidcProvider>> GetEnabledProvidersAsync()
    {
        if (DateTimeOffset.UtcNow < _cacheExpiresAt) return _cachedProviders;

        await _refreshLock.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt) return _cachedProviders;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _cachedProviders = await db.OidcProviders.Where(p => p.IsEnabled).AsNoTracking().ToListAsync();
            _cacheExpiresAt = DateTimeOffset.UtcNow.Add(ProviderCacheDuration);
            return _cachedProviders;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private ConfigurationManager<OpenIdConnectConfiguration> GetConfigManager(OidcProvider provider)
    {
        return _configManagers.GetOrAdd(provider.Authority, authority =>
        {
            var retriever = new HttpDocumentRetriever { RequireHttps = provider.RequireHttpsMetadata };
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                retriever);
        });
    }
}
