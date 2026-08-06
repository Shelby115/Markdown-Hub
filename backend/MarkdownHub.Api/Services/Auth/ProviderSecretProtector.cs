using Microsoft.AspNetCore.DataProtection;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Encrypts/decrypts AuthenticationProvider client secrets at rest using ASP.NET Core's Data
/// Protection framework (see Auth.md §24 - secrets must never be stored/returned in plaintext).
/// The Data Protection key ring must be persisted to a durable path (see Program.cs's
/// AddDataProtection().PersistKeysToFileSystem call and the "keys-data" Docker volume) - without
/// that, every container restart would generate a new key and every previously-stored secret
/// would become permanently undecryptable.
/// </summary>
public class ProviderSecretProtector
{
    private const string Purpose = "MarkdownHub.AuthenticationProvider.ClientSecret.v1";
    private readonly IDataProtector _protector;

    public ProviderSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
