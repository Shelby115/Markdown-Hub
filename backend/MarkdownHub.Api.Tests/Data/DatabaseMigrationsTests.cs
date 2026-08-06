using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Data;

/// <summary>
/// Exercises DatabaseMigrations against a real SQLite database file - not EF Core's InMemory
/// provider, which every other test in this project uses for speed/simplicity but which never
/// touches real SQL or real table names. That gap let a real bug reach production: a manual
/// migration created a table named "AppSettings" while EF's DbSet-property-name convention
/// queries a table named "Settings", so every read/write 500'd with "no such table". These
/// tests specifically guard against that class of mismatch recurring.
/// </summary>
public class DatabaseMigrationsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _hubRoot;

    public DatabaseMigrationsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"markdown-hub-migration-test-{Guid.NewGuid():N}.db");
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
    }

    private (AppDbContext db, SearchIndexService search) NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var db = new AppDbContext(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = $"Data Source={_dbPath}" })
            .Build();
        return (db, new SearchIndexService(config));
    }

    [Fact]
    public async Task ApplyAsync_OnAFreshDatabase_EveryDbSetIsQueryableAfterward()
    {
        var (db, search) = NewContext();
        await using var _ = db;

        await DatabaseMigrations.ApplyAsync(db, search);

        // The specific regression: querying the Settings DbSet must not throw "no such table".
        await db.Settings.FirstOrDefaultAsync();
        await db.Users.FirstOrDefaultAsync();
        await db.Pages.FirstOrDefaultAsync();
        await db.FolderPermissions.FirstOrDefaultAsync();
        await db.ConflictFiles.FirstOrDefaultAsync();
        await db.AuditLog.FirstOrDefaultAsync();
        await db.Backups.FirstOrDefaultAsync();
        await db.AuthenticationProviders.FirstOrDefaultAsync();
        await db.AuthenticationIdentities.FirstOrDefaultAsync();
        await db.Sessions.FirstOrDefaultAsync();
    }

    /// <summary>
    /// This is the actual production failure mode: EnsureCreatedAsync only creates tables when
    /// the database doesn't exist *at all* yet - on a database that already exists (has at
    /// least one table) because it predates some entity being added to the model, it's a
    /// complete no-op, and establishing that entity's table is entirely on the manual
    /// statements below it. A fresh-database test can't catch a table-name mismatch in those
    /// manual statements, because EnsureCreatedAsync would silently create the correctly-named
    /// table anyway before the manual (wrong) statement ever runs. Simulating a pre-existing
    /// database (one real table, created outside EF) forces reliance on the manual statements,
    /// the same way the real, already-running database was never going to get a fresh
    /// EnsureCreatedAsync pass again.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_OnADatabaseThatAlreadyExistsWithoutTheSettingsTable_StillCreatesIt()
    {
        // First pass: establish a normal, fully-migrated database (as if the app had already
        // been running - every table, including Settings, present and correct).
        var (db1, search1) = NewContext();
        await using (db1)
        {
            await DatabaseMigrations.ApplyAsync(db1, search1);
            // Drop just Settings, simulating a database that predates that table specifically -
            // everything else about the schema is otherwise fully migrated and pre-existing,
            // same as the real database this bug happened against.
            await db1.Database.ExecuteSqlRawAsync("DROP TABLE Settings;");
        }

        // Second pass: EnsureCreatedAsync is now a no-op (the database file already exists with
        // tables), so re-establishing Settings depends entirely on the manual statements below it.
        var (db2, search2) = NewContext();
        await using var _ = db2;
        await DatabaseMigrations.ApplyAsync(db2, search2);

        await db2.Settings.FirstOrDefaultAsync(); // must not throw "no such table: Settings"
    }

    [Fact]
    public async Task ApplyAsync_CanReadAndWriteSettingsRoundTrip()
    {
        var (db, search) = NewContext();
        await using var _ = db;
        await DatabaseMigrations.ApplyAsync(db, search);

        db.Settings.Add(new AppSetting { Key = AppSetting.AiOllamaModelKey, Value = "gpt-oss:20b" });
        await db.SaveChangesAsync();

        var reloaded = await db.Settings.FirstOrDefaultAsync(s => s.Key == AppSetting.AiOllamaModelKey);
        Assert.Equal("gpt-oss:20b", reloaded?.Value);
    }

    [Fact]
    public async Task ApplyAsync_RunTwice_IsIdempotent()
    {
        var (db1, search1) = NewContext();
        await using (db1)
        {
            await DatabaseMigrations.ApplyAsync(db1, search1);
        }

        var (db2, search2) = NewContext();
        await using var _ = db2;
        // Must not throw (duplicate column / table already exists) the second time around.
        await DatabaseMigrations.ApplyAsync(db2, search2);
        await db2.Settings.FirstOrDefaultAsync();
    }

    /// <summary>
    /// Same class of bug as the Settings-table regression above, for the tables/columns the
    /// Version History and Activity Log feature added: DocumentVersions is a brand-new table,
    /// and Pages/AuditLog both gained new columns. A database that predates this feature (has
    /// every other table, but none of these) must still end up with a fully working schema
    /// after ApplyAsync - EnsureCreatedAsync alone will not add any of it once the database
    /// file already exists.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_OnADatabaseThatPredatesVersionHistory_AddsTheNewTableAndColumns()
    {
        var (db1, search1) = NewContext();
        await using (db1)
        {
            await DatabaseMigrations.ApplyAsync(db1, search1);
            // Simulate a database from before this feature existed: drop the new table and the
            // new columns, leaving everything else as a normal, fully-migrated pre-existing
            // database. Dropping columns in place (rather than recreating the tables) avoids
            // disturbing PageLinks' foreign key to Pages.
            await db1.Database.ExecuteSqlRawAsync("DROP TABLE DocumentVersions;");
            // The partial unique index references IsDeleted - drop it first so dropping that
            // column doesn't fail with "error in index ... no such column"; ApplyAsync recreates
            // this same index unconditionally on the way back in.
            await db1.Database.ExecuteSqlRawAsync("DROP INDEX IX_Pages_RelativePath;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE Pages DROP COLUMN IsDeleted;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE Pages DROP COLUMN DeletedAtUtc;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE Pages DROP COLUMN DeletedByAppUserId;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN ObjectType;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN ObjectId;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN IpAddress;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN RelatedVersionId;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN OccurrenceCount;");
            await db1.Database.ExecuteSqlRawAsync("ALTER TABLE AuditLog DROP COLUMN LastOccurredAtUtc;");
        }

        var (db2, search2) = NewContext();
        await using var _ = db2;
        await DatabaseMigrations.ApplyAsync(db2, search2);

        // Must not throw "no such table"/"no such column" for any of it.
        await db2.DocumentVersions.FirstOrDefaultAsync();
        var page = new PageMetadata { RelativePath = "Test.md", PageName = "Test", IsDeleted = true, DeletedAtUtc = DateTimeOffset.UtcNow, DeletedByAppUserId = 1 };
        db2.Pages.Add(page);
        await db2.SaveChangesAsync();
        var auditEntry = new AuditLogEntry { Action = "Test", ObjectType = "Document", ObjectId = 1, IpAddress = "127.0.0.1", RelatedVersionId = 1, OccurrenceCount = 2 };
        db2.AuditLog.Add(auditEntry);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task ApplyAsync_ActivePagesAtTheSamePathAsASoftDeletedOne_BothCoexist()
    {
        var (db, search) = NewContext();
        await using var _ = db;
        await DatabaseMigrations.ApplyAsync(db, search);

        db.Pages.Add(new PageMetadata { RelativePath = "Notes.md", PageName = "Notes", IsDeleted = true, DeletedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // A second, active page at the exact same path must be allowed - the unique index is
        // scoped to active (non-deleted) rows only.
        db.Pages.Add(new PageMetadata { RelativePath = "Notes.md", PageName = "Notes" });
        await db.SaveChangesAsync(); // must not throw a unique constraint violation

        Assert.Equal(2, await db.Pages.CountAsync(p => p.RelativePath == "Notes.md"));
    }

    [Fact]
    public async Task ApplyAsync_DropsAnEarlierWronglyNamedAppSettingsTable()
    {
        var (db, search) = NewContext();
        await using var _ = db;
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE AppSettings (Id INTEGER PRIMARY KEY, Key TEXT, Value TEXT);");

        await DatabaseMigrations.ApplyAsync(db, search);

        // Should not throw - the stray table is gone, "Settings" is what's actually used.
        await db.Settings.FirstOrDefaultAsync();
    }

    /// <summary>Builds a database that looks like a fully-migrated pre-auth-redesign install:
    /// every ordinary table (Pages, AuditLog, etc.) present via EnsureCreatedAsync against the
    /// *current* model (their shape is irrelevant to these tests), then the auth-specific parts
    /// reverted to their old shape - Users.KeycloakSubjectId instead of the new columns, and the
    /// old single-table OidcProviders instead of AuthenticationProviders/Identities/Sessions -
    /// since EnsureCreatedAsync against the current model can only ever produce the new schema
    /// for those. This is the only way to exercise the legacy-data migration path at all.</summary>
    private static async Task CreateLegacyPreAuthRedesignDatabaseAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        await db.Database.ExecuteSqlRawAsync("DROP TABLE AuthenticationIdentities;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE Sessions;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE AuthenticationProviders;");

        await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Users_NormalizedUsername;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN PasswordHash;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN NormalizedUsername;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN NormalizedEmail;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN DisplayName;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN UpdatedAt;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN KeycloakSubjectId TEXT NOT NULL DEFAULT '';");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_Users_KeycloakSubjectId ON Users (KeycloakSubjectId);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE OidcProviders (
                Id INTEGER NOT NULL CONSTRAINT PK_OidcProviders PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Authority TEXT NOT NULL,
                ClientId TEXT NOT NULL,
                Audience TEXT NOT NULL,
                RequireHttpsMetadata INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            """);
    }

    [Fact]
    public async Task ApplyAsync_LegacySingleProvider_MigratesProviderDisabledAndLinksExistingIdentities()
    {
        var (db1, search1) = NewContext();
        await using (db1)
        {
            await CreateLegacyPreAuthRedesignDatabaseAsync(db1);
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO Users (KeycloakSubjectId, Username, Email, IsAdministrator, IsDisabled, CreatedAt) " +
                "VALUES ('legacy-sub-1', 'alice', 'alice@example.com', 1, 0, '2024-01-01T00:00:00Z');");
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO Users (KeycloakSubjectId, Username, Email, IsAdministrator, IsDisabled, CreatedAt) " +
                "VALUES ('pending:bob', 'bob', NULL, 0, 0, '2024-01-01T00:00:00Z');");
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO OidcProviders (Name, Authority, ClientId, Audience, RequireHttpsMetadata, IsEnabled, CreatedAt) " +
                "VALUES ('Keycloak', 'https://auth.example.com/realms/markdown-hub', 'markdown-hub', 'markdown-hub', 1, 1, '2024-01-01T00:00:00Z');");
        }

        var (db2, search2) = NewContext();
        await using var _ = db2;
        await DatabaseMigrations.ApplyAsync(db2, search2);

        var provider = Assert.Single(db2.AuthenticationProviders);
        Assert.Equal("Keycloak", provider.DisplayName);
        Assert.False(provider.Enabled); // no client secret exists yet - must not come up silently usable
        Assert.Null(provider.ClientSecretProtected);

        var identity = Assert.Single(db2.AuthenticationIdentities);
        Assert.Equal("legacy-sub-1", identity.Subject);
        var alice = await db2.Users.SingleAsync(u => u.Username == "alice");
        Assert.Equal(identity.UserId, alice.Id);
        Assert.Equal("ALICE", alice.NormalizedUsername);
        Assert.True(alice.IsAdministrator);

        // The "pending:" placeholder becomes a plain password-less, identity-less user - exactly
        // the new pre-provisioning shape - rather than getting a bogus AuthenticationIdentity.
        var bob = await db2.Users.SingleAsync(u => u.Username == "bob");
        Assert.Null(bob.PasswordHash);
        Assert.DoesNotContain(db2.AuthenticationIdentities, i => i.UserId == bob.Id);

        // The old column/table are gone.
        var hasKeycloakColumn = await db2.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM pragma_table_info('Users') WHERE name = 'KeycloakSubjectId'").SingleAsync();
        Assert.Equal(0, hasKeycloakColumn);
    }

    /// <summary>Auth.md §29: if an automatic migration can't safely preserve an existing identity
    /// relationship, it must fail clearly rather than silently mis-assigning it. The old schema
    /// never recorded which of several providers a user actually authenticated through, so with
    /// more than one legacy provider present, no AuthenticationIdentity rows should be created at
    /// all - affected users keep their account but need an admin-issued temporary password.</summary>
    [Fact]
    public async Task ApplyAsync_LegacyMultipleProviders_MigratesProvidersButLeavesIdentitiesUnlinked()
    {
        var (db1, search1) = NewContext();
        await using (db1)
        {
            await CreateLegacyPreAuthRedesignDatabaseAsync(db1);
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO Users (KeycloakSubjectId, Username, Email, IsAdministrator, IsDisabled, CreatedAt) " +
                "VALUES ('legacy-sub-1', 'alice', NULL, 0, 0, '2024-01-01T00:00:00Z');");
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO OidcProviders (Name, Authority, ClientId, Audience, RequireHttpsMetadata, IsEnabled, CreatedAt) " +
                "VALUES ('Keycloak', 'https://auth.example.com/realms/markdown-hub', 'markdown-hub', 'markdown-hub', 1, 1, '2024-01-01T00:00:00Z');");
            await db1.Database.ExecuteSqlRawAsync(
                "INSERT INTO OidcProviders (Name, Authority, ClientId, Audience, RequireHttpsMetadata, IsEnabled, CreatedAt) " +
                "VALUES ('Google', 'https://accounts.google.com', 'markdown-hub-2', 'markdown-hub-2', 1, 1, '2024-01-01T00:00:00Z');");
        }

        var (db2, search2) = NewContext();
        await using var _ = db2;
        await DatabaseMigrations.ApplyAsync(db2, search2);

        Assert.Equal(2, await db2.AuthenticationProviders.CountAsync());
        Assert.Empty(db2.AuthenticationIdentities);
        var alice = await db2.Users.SingleAsync(u => u.Username == "alice");
        Assert.Null(alice.PasswordHash); // keeps her account; needs an admin-issued temp password to get back in
    }

    [Fact]
    public async Task ApplyAsync_NoLegacyOidcProvidersTable_SkipsMigrationCleanly()
    {
        // A brand-new install (or a database already past this migration) has no OidcProviders
        // table at all - must not throw trying to read from it.
        var (db, search) = NewContext();
        await using var _ = db;

        await DatabaseMigrations.ApplyAsync(db, search);

        Assert.Empty(db.AuthenticationProviders);
        Assert.Empty(db.AuthenticationIdentities);
    }
}
