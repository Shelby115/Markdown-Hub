using Microsoft.EntityFrameworkCore;
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

        // OIDC providers - table name must match the DbSet property name (AppDbContext.OidcProviders),
        // same convention/pitfall as Settings above.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS OidcProviders (
                Id INTEGER NOT NULL CONSTRAINT PK_OidcProviders PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Authority TEXT NOT NULL,
                ClientId TEXT NOT NULL,
                Audience TEXT NOT NULL,
                RequireHttpsMetadata INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            """, ct);

        await search.EnsureSchemaAsync(ct);
    }
}
