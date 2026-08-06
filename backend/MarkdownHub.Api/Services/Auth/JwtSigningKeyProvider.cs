using Microsoft.IdentityModel.Tokens;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Holds the app's single self-signing JWT key for the JwtBearer options pipeline to read
/// lazily (see Program.cs's AddOptions&lt;JwtBearerOptions&gt;().Configure&lt;T&gt; wiring -
/// the same DI-aware-options idiom the old multi-issuer OidcProviderValidationService used).
/// The key itself is resolved/persisted once at startup by AppTokenService.GetSigningKeyAsync
/// and pushed in here before the app starts accepting requests - unlike the old per-issuer
/// validation, there is now exactly one fixed issuer/key, so no per-request DB lookup is needed.
/// </summary>
public class JwtSigningKeyProvider
{
    private byte[]? _key;

    public void SetKey(byte[] key) => _key = key;

    public SymmetricSecurityKey GetKey() =>
        new(_key ?? throw new InvalidOperationException("JWT signing key has not been initialized yet."));
}
