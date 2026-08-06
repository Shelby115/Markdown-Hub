namespace MarkdownHub.Api.Services;

/// <summary>Derives a stable URL-safe AuthenticationProvider.Name slug from an admin-entered
/// display name (or a legacy provider's name during migration) - used in routes/redirect URIs.</summary>
public static class ProviderNameSlug
{
    public static string Create(string name, string fallback = "provider")
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? fallback : slug;
    }
}
