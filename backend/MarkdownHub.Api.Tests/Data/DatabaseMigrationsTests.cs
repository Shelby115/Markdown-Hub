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
        await db.OidcProviders.FirstOrDefaultAsync();
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
}
