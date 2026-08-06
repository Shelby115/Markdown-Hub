using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Drives the server-side authorization-code exchange for both OIDC and generic OAuth 2.0
/// providers (Auth.md §11/§12) - the app itself is the confidential client; provider tokens and
/// client secrets never reach the browser. Purely protocol mechanics: callers (AuthController)
/// own looking up/creating the application AppUser/AuthenticationIdentity rows.
/// </summary>
public class ExternalAuthService
{
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> DiscoveryCache = new();
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProviderSecretProtector _secretProtector;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ExternalAuthService(IHttpClientFactory httpClientFactory, ProviderSecretProtector secretProtector, IDataProtectionProvider dataProtectionProvider)
    {
        _httpClientFactory = httpClientFactory;
        _secretProtector = secretProtector;
        _dataProtectionProvider = dataProtectionProvider;
    }

    private IDataProtector StateProtector => _dataProtectionProvider.CreateProtector("MarkdownHub.Auth.ExternalState.v1");

    public static ProviderConfiguration ParseConfiguration(AuthenticationProvider provider) =>
        ParseConfiguration(provider.ConfigurationJson);

    public static ProviderConfiguration ParseConfiguration(string configurationJson) =>
        JsonSerializer.Deserialize<ProviderConfiguration>(configurationJson) ?? new ProviderConfiguration();

    public async Task<string> BuildAuthorizationUrlAsync(
        AuthenticationProvider provider, string redirectUri, AuthIntent intent, int? linkUserId, string returnOrigin, CancellationToken ct)
    {
        var config = ParseConfiguration(provider);
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var oidcNonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

        var state = new ExternalAuthState(provider.Name, intent, linkUserId, codeVerifier, oidcNonce, returnOrigin, DateTimeOffset.UtcNow);
        var protectedState = StateProtector.Protect(JsonSerializer.Serialize(state));

        string authorizationEndpoint;
        if (provider.Type == AuthProviderType.Oidc)
        {
            var discovery = await GetOidcConfigurationAsync(provider, config, ct);
            authorizationEndpoint = config.AuthorizationEndpoint ?? discovery.AuthorizationEndpoint;
        }
        else
        {
            authorizationEndpoint = config.AuthorizationEndpoint
                ?? throw new InvalidOperationException($"Provider '{provider.DisplayName}' has no authorization endpoint configured.");
        }

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = config.Scopes,
            ["state"] = protectedState,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        if (provider.Type == AuthProviderType.Oidc)
        {
            query["nonce"] = oidcNonce;
        }

        return QueryHelpers.AddQueryString(authorizationEndpoint, query);
    }

    /// <summary>Decrypts and validates a "state" round-trip value. Throws if it was tampered
    /// with, forged, or has expired (Auth.md §20's state-validation requirement).</summary>
    public ExternalAuthState UnprotectState(string protectedState)
    {
        string json;
        try
        {
            json = StateProtector.Unprotect(protectedState);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Sign-in request could not be verified.", ex);
        }

        var state = JsonSerializer.Deserialize<ExternalAuthState>(json)
            ?? throw new InvalidOperationException("Sign-in request could not be verified.");
        if (DateTimeOffset.UtcNow - state.IssuedAt > StateLifetime)
        {
            throw new InvalidOperationException("Sign-in attempt expired - please try again.");
        }

        return state;
    }

    public async Task<ExternalIdentity> ExchangeCodeAsync(
        AuthenticationProvider provider, string code, string redirectUri, ExternalAuthState state, CancellationToken ct)
    {
        var config = ParseConfiguration(provider);
        var client = _httpClientFactory.CreateClient();
        var secret = provider.ClientSecretProtected is not null ? _secretProtector.Unprotect(provider.ClientSecretProtected) : null;

        OpenIdConnectConfiguration? discovery = null;
        string tokenEndpoint;
        if (provider.Type == AuthProviderType.Oidc)
        {
            discovery = await GetOidcConfigurationAsync(provider, config, ct);
            tokenEndpoint = config.TokenEndpoint ?? discovery.TokenEndpoint;
        }
        else
        {
            tokenEndpoint = config.TokenEndpoint
                ?? throw new InvalidOperationException($"Provider '{provider.DisplayName}' has no token endpoint configured.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = provider.ClientId,
            ["code_verifier"] = state.CodeVerifier,
        };
        if (!string.IsNullOrEmpty(secret))
        {
            form["client_secret"] = secret;
        }

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var tokenResponse = await client.SendAsync(tokenRequest, ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Provider '{provider.DisplayName}' rejected the authorization code.");
        }

        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var accessToken = payload.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var idToken = payload.TryGetProperty("id_token", out var it) ? it.GetString() : null;

        if (provider.Type == AuthProviderType.Oidc && !string.IsNullOrEmpty(idToken))
        {
            return ValidateIdToken(provider, config, discovery!, idToken, state.OidcNonce);
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException($"Provider '{provider.DisplayName}' did not return a usable token.");
        }

        return await FetchUserInfoAsync(client, config, accessToken, ct);
    }

    private static ExternalIdentity ValidateIdToken(
        AuthenticationProvider provider, ProviderConfiguration config, OpenIdConnectConfiguration discovery, string idToken, string expectedNonce)
    {
        // Per the OIDC spec, an id_token's "aud" always contains the client id - that must
        // always be accepted regardless of what's configured. The admin-configured Audience
        // field is an *additional* accepted value on top of that, for providers with a custom
        // audience mapper also applied to the id_token - it must never *replace* the client id
        // check, since a provider's default id_token won't carry a custom value that was only
        // ever mapped onto access tokens (this exact mismatch is what a migrated pre-redesign
        // provider's Audience field means - it was validated against access tokens under the
        // old architecture, not id_tokens).
        var validAudiences = config.Audience is { Length: > 0 } configuredAudience
            ? new[] { provider.ClientId, configuredAudience }
            : [provider.ClientId];

        // JwtSecurityTokenHandler remaps well-known claim names to long legacy XML claim-type
        // URIs by default (e.g. "sub" -> ClaimTypes.NameIdentifier, "email" -> ClaimTypes.Email)
        // unless told not to - without this, every FindFirstValue("sub"/"email"/"name") call
        // below would silently return null even for a perfectly valid, correctly-signed token.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidAudiences = validAudiences,
            IssuerSigningKeys = discovery.SigningKeys,
            ValidateLifetime = true,
        };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Provider '{provider.DisplayName}' returned an invalid identity token.", ex);
        }

        var nonce = principal.FindFirstValue("nonce");
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Identity token nonce mismatch.");
        }

        var subject = principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Identity token has no subject.");
        var email = principal.FindFirstValue("email");
        var name = principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username");
        return new ExternalIdentity(subject, email, name);
    }

    private static async Task<ExternalIdentity> FetchUserInfoAsync(HttpClient client, ProviderConfiguration config, string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.UserInfoEndpoint))
        {
            throw new InvalidOperationException("Provider has no userinfo endpoint configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, config.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Couldn't retrieve profile information from the provider.");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var subject = GetField(json, config.UserIdField)
            ?? throw new InvalidOperationException("Provider response has no subject/user id field.");
        var email = GetField(json, config.EmailField);
        var name = GetField(json, config.NameField);
        return new ExternalIdentity(subject, email, name);
    }

    private static string? GetField(JsonElement root, string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty(fieldName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private async Task<OpenIdConnectConfiguration> GetOidcConfigurationAsync(AuthenticationProvider provider, ProviderConfiguration config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.Authority))
        {
            throw new InvalidOperationException($"Provider '{provider.DisplayName}' has no authority configured.");
        }

        var cacheKey = $"{provider.Name}|{config.Authority}";
        var manager = DiscoveryCache.GetOrAdd(cacheKey, _ =>
        {
            // Explicitly supply an HttpClient from the injected factory - HttpDocumentRetriever's
            // parameterless constructor otherwise builds its own internal client, bypassing the
            // factory entirely (and making this unmockable in tests).
            var retriever = new HttpDocumentRetriever(_httpClientFactory.CreateClient()) { RequireHttps = config.RequireHttpsMetadata };
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{config.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), retriever);
        });
        return await manager.GetConfigurationAsync(ct);
    }

    private static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string ComputeCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
