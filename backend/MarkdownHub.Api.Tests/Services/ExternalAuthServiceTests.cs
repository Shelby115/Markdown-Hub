using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

/// <summary>
/// Exercises ExternalAuthService.ExchangeCodeAsync's OIDC id_token validation against a fake but
/// real (signed, RSA) token over a fake discovery/JWKS/token-endpoint HTTP pipeline - not just
/// the AuthController-level tests, which never actually validate a token. This is the exact path
/// that broke in production: a provider migrated from the pre-redesign schema carries an
/// "Audience" value that was only ever valid for *access* tokens under the old architecture, and
/// validating the id_token against that value alone (instead of also always accepting the client
/// id, which the OIDC spec guarantees "aud" contains) rejected a perfectly valid sign-in.
/// </summary>
public class ExternalAuthServiceTests
{
    private const string ClientId = "markdown-hub";

    [Fact]
    public async Task ExchangeCodeAsync_IdTokenAudienceIsClientId_SucceedsDespiteMismatchedConfiguredAudience()
    {
        // Reproduces the production bug: a migrated provider's Audience is a leftover
        // access-token-only value ("markdown-hub-audience") that will never appear in a real
        // id_token, whose "aud" is simply the client id per spec.
        var identity = await ExchangeWithFakeProviderAsync(
            issuer: $"https://idp.example.com/realms/{Guid.NewGuid():N}",
            configuredAudience: "markdown-hub-audience",
            idTokenAudience: ClientId);

        Assert.Equal("user-123", identity.Subject);
        Assert.Equal("user@example.com", identity.Email);
    }

    [Fact]
    public async Task ExchangeCodeAsync_IdTokenAudienceMatchesConfiguredAudienceOnly_Succeeds()
    {
        // A provider with a custom audience mapper also applied to the id_token - the configured
        // Audience must still be accepted, not just the client id.
        var identity = await ExchangeWithFakeProviderAsync(
            issuer: $"https://idp.example.com/realms/{Guid.NewGuid():N}",
            configuredAudience: "custom-audience",
            idTokenAudience: "custom-audience");

        Assert.Equal("user-123", identity.Subject);
    }

    [Fact]
    public async Task ExchangeCodeAsync_IdTokenAudienceMatchesNeitherClientIdNorConfiguredAudience_Throws()
    {
        var issuer = $"https://idp.example.com/realms/{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExchangeWithFakeProviderAsync(
            issuer, configuredAudience: "markdown-hub-audience", idTokenAudience: "some-other-client"));
    }

    private static async Task<ExternalIdentity> ExchangeWithFakeProviderAsync(string issuer, string? configuredAudience, string idTokenAudience)
    {
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var idToken = BuildIdToken(issuer, idTokenAudience, signingKey, nonce: "test-nonce");

        var provider = new AuthenticationProvider
        {
            Name = $"test-{Guid.NewGuid():N}",
            DisplayName = "Test",
            Type = AuthProviderType.Oidc,
            ClientId = ClientId,
            ConfigurationJson = JsonSerializer.Serialize(new ProviderConfiguration
            {
                Authority = issuer,
                Audience = configuredAudience,
            }),
        };

        var httpClientFactory = new FakeHttpClientFactory(new FakeOidcHandler(issuer, signingKey, idToken));
        var secretProtector = new ProviderSecretProtector(new EphemeralDataProtectionProvider());
        var sut = new ExternalAuthService(httpClientFactory, secretProtector, new EphemeralDataProtectionProvider());

        var state = new ExternalAuthState(provider.Name, AuthIntent.Login, null, "verifier", "test-nonce", "http://localhost", DateTimeOffset.UtcNow);
        return await sut.ExchangeCodeAsync(provider, "fake-code", "http://localhost/callback", state, CancellationToken.None);
    }

    private static string BuildIdToken(string issuer, string audience, RsaSecurityKey key, string nonce)
    {
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim("sub", "user-123"),
            new Claim("email", "user@example.com"),
            new Claim("name", "Test User"),
            new Claim("nonce", nonce),
        };
        var token = new JwtSecurityToken(issuer, audience, claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private class FakeOidcHandler : HttpMessageHandler
    {
        private readonly string _issuer;
        private readonly RsaSecurityKey _key;
        private readonly string _idToken;

        public FakeOidcHandler(string issuer, RsaSecurityKey key, string idToken)
        {
            _issuer = issuer;
            _key = key;
            _idToken = idToken;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(new
                {
                    issuer = _issuer,
                    authorization_endpoint = $"{_issuer}/protocol/openid-connect/auth",
                    token_endpoint = $"{_issuer}/protocol/openid-connect/token",
                    jwks_uri = $"{_issuer}/protocol/openid-connect/certs",
                }));
            }
            if (path.EndsWith("/protocol/openid-connect/certs", StringComparison.Ordinal))
            {
                var parameters = _key.Rsa!.ExportParameters(false);
                return Task.FromResult(JsonResponse(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = _key.KeyId,
                            n = Base64UrlEncoder.Encode(parameters.Modulus),
                            e = Base64UrlEncoder.Encode(parameters.Exponent),
                            alg = "RS256",
                        },
                    },
                }));
            }
            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(new { access_token = "fake-access-token", id_token = _idToken, token_type = "Bearer" }));
            }
            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        }

        private static HttpResponseMessage JsonResponse(object body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}
