namespace MarkdownHub.Api.Data.Entities.Auth;

public enum AuthProviderType
{
    Oidc = 0,
    OAuth2 = 1,
}

/// <summary>What happens when someone authenticates through this provider for the first time
/// (no AuthenticationIdentity yet matches their subject) and no application account is linked.</summary>
public enum AutoProvisionPolicy
{
    /// <summary>Create a new, immediately-usable application account.</summary>
    Allow = 0,

    /// <summary>Create the account but leave it disabled until an administrator enables it.</summary>
    RequireApproval = 1,

    /// <summary>Refuse the sign-in; only accounts that already have this identity linked
    /// (via the self-service linking flow) may authenticate through this provider.</summary>
    Disabled = 2,
}
