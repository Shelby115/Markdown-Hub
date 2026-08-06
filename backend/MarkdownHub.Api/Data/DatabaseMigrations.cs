using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Data;

/// <summary>
/// Applies the app's startup schema setup: EnsureCreatedAsync (creates the database file and any
/// entirely-missing tables) plus a set of manual, idempotent statements for everything
/// EnsureCreatedAsync can't do on an already-existing database - new columns on existing tables,
/// and (as of the Settings table) tables it apparently doesn't add either once the database file
/// already exists. Extracted from Program.cs so it can run against a real SQLite database in
/// tests, not just EF Core's InMemory provider - which doesn't touch real SQL or real table
/// names, and so can't catch a table name that doesn't match EF's DbSet-property-name
/// convention. That exact mismatch broke the app's AI settings endpoint in production once
/// already (see git history) precisely because InMemory-provider tests couldn't see it.
/// </summary>
public static class DatabaseMigrations
{
    public static async Task ApplyAsync(AppDbContext db, SearchIndexService search, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        async Task AddColumnIfMissingAsync(string sql)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // Already applied on a previous startup - nothing to do.
            }
        }

        async Task DropColumnIfPresentAsync(string sql)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // Already dropped (or the table never had it - a brand-new database) - nothing to do.
            }
        }

        await AddColumnIfMissingAsync("ALTER TABLE Pages ADD COLUMN IsTemplate INTEGER NOT NULL DEFAULT 0;");
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN DefaultFolderPath TEXT NULL;");

        // Version History / Activity Log feature - soft-delete columns on Pages so a deleted
        // document's history stays recoverable (PageMetadata.Id is the stable "document ID"
        // version rows are keyed on; hard-deleting the row would orphan/destroy that history).
        await AddColumnIfMissingAsync("ALTER TABLE Pages ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;");
        await AddColumnIfMissingAsync("ALTER TABLE Pages ADD COLUMN DeletedAtUtc TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE Pages ADD COLUMN DeletedByAppUserId INTEGER NULL;");

        // Replace the old blanket-unique index on RelativePath with one that's only unique
        // among active pages, now that a soft-deleted row can coexist with a new page later
        // created at the same path (see PageMetadata.IsDeleted).
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Pages_RelativePath;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Pages_RelativePath ON Pages (RelativePath) WHERE IsDeleted = 0;", ct);

        // AuditLogEntry doubles as the Activity Log's event table - extend it in place rather
        // than standing up a second, parallel "ActivityEvent" table.
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN ObjectType TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN ObjectId INTEGER NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN IpAddress TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN RelatedVersionId INTEGER NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN OccurrenceCount INTEGER NOT NULL DEFAULT 1;");
        await AddColumnIfMissingAsync("ALTER TABLE AuditLog ADD COLUMN LastOccurredAtUtc TEXT NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_AuditLog_Timestamp ON AuditLog (Timestamp);", ct);

        // New in this database version - EnsureCreatedAsync only creates tables for a database
        // file that doesn't exist yet at all (see the Settings table precedent below), so an
        // existing database needs this created manually too.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS DocumentVersions (
                Id INTEGER NOT NULL CONSTRAINT PK_DocumentVersions PRIMARY KEY AUTOINCREMENT,
                DocumentId INTEGER NOT NULL,
                UserId INTEGER NULL,
                Content TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                IsOpen INTEGER NOT NULL,
                VersionType TEXT NOT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_DocumentVersions_DocumentId_IsOpen ON DocumentVersions (DocumentId, IsOpen);", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_DocumentVersions_CreatedAtUtc ON DocumentVersions (CreatedAtUtc);", ct);

        // EF Core names a table after its DbSet property by convention (AppDbContext.Settings),
        // not after the entity class (AppSetting) - this table must be named "Settings" to match
        // what EF actually queries, or every read/write against it 500s with "no such table".
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS AppSettings;", ct); // cleans up an earlier wrongly-named attempt
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER NOT NULL CONSTRAINT PK_Settings PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Settings_Key ON Settings (Key);", ct);

        // --- Auth redesign (local-first identity, linked external providers - see Auth.md) ---
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN NormalizedUsername TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN NormalizedEmail TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN DisplayName TEXT NULL;");
        await AddColumnIfMissingAsync("ALTER TABLE Users ADD COLUMN UpdatedAt TEXT NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET NormalizedUsername = UPPER(TRIM(Username)) WHERE NormalizedUsername IS NULL;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET NormalizedEmail = UPPER(TRIM(Email)) WHERE NormalizedEmail IS NULL AND Email IS NOT NULL;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;", ct);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Users_KeycloakSubjectId;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_NormalizedUsername ON Users (NormalizedUsername);", ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AuthenticationProviders (
                Id INTEGER NOT NULL CONSTRAINT PK_AuthenticationProviders PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Type INTEGER NOT NULL,
                ClientId TEXT NOT NULL,
                ClientSecretProtected TEXT NULL,
                ConfigurationJson TEXT NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AuthenticationProviders_Name ON AuthenticationProviders (Name);", ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AuthenticationIdentities (
                Id INTEGER NOT NULL CONSTRAINT PK_AuthenticationIdentities PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                AuthenticationProviderId INTEGER NOT NULL,
                Subject TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AuthenticationIdentities_Provider_Subject ON AuthenticationIdentities (AuthenticationProviderId, Subject);", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_AuthenticationIdentities_UserId ON AuthenticationIdentities (UserId);", ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Sessions (
                Id TEXT NOT NULL CONSTRAINT PK_Sessions PRIMARY KEY,
                UserId INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                LastActivityAt TEXT NOT NULL,
                RevokedAt TEXT NULL,
                UserAgent TEXT NULL,
                IpAddress TEXT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_Sessions_UserId ON Sessions (UserId);", ct);

        // Migrate the old single-table OIDC-provider/bearer-JWT model into the new
        // AuthenticationProviders/AuthenticationIdentities shape, if an old database is present.
        // Runs at most once (gated on AuthenticationProviders being empty) - after that the new
        // tables are authoritative regardless of whether the old OidcProviders table still exists.
        if (await TableExistsAsync(db, "OidcProviders", ct) && !await db.AuthenticationProviders.AnyAsync(ct))
        {
            await MigrateLegacyOidcDataAsync(db, ct);
        }

        // Superseded by AuthenticationProviders/AuthenticationIdentities above - drop last, after
        // any migratable data has already been copied out.
        await DropColumnIfPresentAsync("ALTER TABLE Users DROP COLUMN KeycloakSubjectId;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS OidcProviders;", ct);

        await search.EnsureSchemaAsync(ct);
    }

    private sealed record LegacyOidcProviderRow(
        int Id, string Name, string Authority, string ClientId, string Audience,
        bool RequireHttpsMetadata, DateTimeOffset CreatedAt);

    /// <summary>
    /// Copies the old single-table OIDC provider config into the new AuthenticationProviders
    /// shape (disabled - no client secret exists yet, since the old model never needed one for
    /// its SPA-driven public-client PKCE flow; an administrator must supply one, plus update the
    /// provider's redirect URI, before the migrated provider can be enabled again) and, where it
    /// can be done unambiguously, migrates each user's legacy Keycloak subject id into an
    /// AuthenticationIdentity linked to that provider.
    ///
    /// The old schema never recorded *which* provider issued a given user's subject (bearer
    /// tokens were validated purely by matching the token's own "iss" claim against whichever
    /// provider was enabled at request time) - so if more than one legacy provider existed, this
    /// migration cannot safely guess which one any given user authenticated through. Per
    /// Auth.md §29 ("if an automatic migration cannot safely preserve an existing identity
    /// relationship, the migration should fail clearly rather than silently creating duplicate
    /// accounts"), that case migrates the provider configs but leaves user identities unlinked -
    /// those users keep their account and permissions, but need an administrator to issue them a
    /// temporary local password (Admin > Users > Set Password) so they can log in once and
    /// re-link their provider themselves.
    /// </summary>
    private static async Task MigrateLegacyOidcDataAsync(AppDbContext db, CancellationToken ct)
    {
        var legacyProviders = await ReadLegacyOidcProvidersAsync(db, ct);
        if (legacyProviders.Count == 0) return;

        var providerIds = new Dictionary<int, int>(); // legacy OidcProviders.Id -> new AuthenticationProviders.Id
        foreach (var legacy in legacyProviders)
        {
            var config = new ProviderConfiguration
            {
                Authority = legacy.Authority,
                RequireHttpsMetadata = legacy.RequireHttpsMetadata,
                Audience = legacy.Audience,
                AutoProvision = AutoProvisionPolicy.Allow,
            };
            var provider = new AuthenticationProvider
            {
                Name = ProviderNameSlug.Create(legacy.Name, $"provider-{legacy.Id}"),
                DisplayName = legacy.Name,
                Type = AuthProviderType.Oidc,
                ClientId = legacy.ClientId,
                ClientSecretProtected = null,
                ConfigurationJson = JsonSerializer.Serialize(config),
                Enabled = false,
                CreatedAt = legacy.CreatedAt,
                UpdatedAt = legacy.CreatedAt,
            };
            db.AuthenticationProviders.Add(provider);
            await db.SaveChangesAsync(ct); // need provider.Id before mapping it below
            providerIds[legacy.Id] = provider.Id;
        }

        if (legacyProviders.Count == 1)
        {
            var newProviderId = providerIds[legacyProviders[0].Id];
            var subjects = await ReadLegacyUserSubjectsAsync(db, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var (userId, subject) in subjects)
            {
                if (subject.StartsWith("pending:", StringComparison.Ordinal)) continue; // becomes a plain pending user instead
                db.AuthenticationIdentities.Add(new AuthenticationIdentity
                {
                    UserId = userId,
                    AuthenticationProviderId = newProviderId,
                    Subject = subject,
                    CreatedAt = now,
                    LastUsedAt = now,
                });
            }
            await db.SaveChangesAsync(ct);
        }
        // else: ambiguous (see doc comment above) - provider configs migrated, identities left unlinked.
    }

    private static async Task<List<LegacyOidcProviderRow>> ReadLegacyOidcProvidersAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = new List<LegacyOidcProviderRow>();
        await WithOpenConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Authority, ClientId, Audience, RequireHttpsMetadata, CreatedAt FROM OidcProviders";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new LegacyOidcProviderRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetInt64(5) != 0, DateTimeOffset.Parse(reader.GetString(6))));
            }
        }, ct);
        return rows;
    }

    private static async Task<List<(int UserId, string Subject)>> ReadLegacyUserSubjectsAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = new List<(int, string)>();
        await WithOpenConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, KeycloakSubjectId FROM Users";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(1)) rows.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        }, ct);
        return rows;
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName, CancellationToken ct)
    {
        var found = false;
        await WithOpenConnectionAsync(db, async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
            var param = cmd.CreateParameter();
            param.ParameterName = "$name";
            param.Value = tableName;
            cmd.Parameters.Add(param);
            found = await cmd.ExecuteScalarAsync(ct) is not null;
        }, ct);
        return found;
    }

    /// <summary>Runs <paramref name="action"/> against the DbContext's own ADO.NET connection
    /// for a raw read that EF's LINQ surface can't express (querying a table with no matching
    /// entity type registered) - opens the connection only if it wasn't already, and closes it
    /// again afterward in that case, leaving EF's own connection-lifecycle management alone
    /// otherwise.</summary>
    private static async Task WithOpenConnectionAsync(AppDbContext db, Func<System.Data.Common.DbConnection, Task> action, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != ConnectionState.Open;
        if (wasClosed) await conn.OpenAsync(ct);
        try
        {
            await action(conn);
        }
        finally
        {
            if (wasClosed) await conn.CloseAsync();
        }
    }
}
