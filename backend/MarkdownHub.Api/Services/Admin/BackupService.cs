using System.IO.Compression;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Produces a single zip archive containing the Markdown hub, the SQLite database,
/// and application configuration - stored under BackupLocation, independent of the
/// live hub directory. Retention is enforced by deleting the oldest backups beyond
/// the configured count after each run.
/// </summary>
public class BackupService
{
    private readonly HubPathService _hub;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<BackupService> _logger;

    public BackupService(HubPathService hub, IConfiguration config, AppDbContext db, ILogger<BackupService> logger)
    {
        _hub = hub;
        _config = config;
        _db = db;
        _logger = logger;
    }

    public async Task<BackupRecord> RunBackupAsync(bool manual, CancellationToken ct = default)
    {
        var backupDir = _config["Hub:BackupLocation"] ?? "/data/backups";
        Directory.CreateDirectory(backupDir);

        var fileName = $"backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var fullPath = Path.Combine(backupDir, fileName);

        using (var zip = ZipFile.Open(fullPath, ZipArchiveMode.Create))
        {
            AddDirectoryToZip(zip, _hub.Root, "markdown");

            var dbPath = ExtractSqlitePath();
            if (File.Exists(dbPath))
            {
                // Copy the db file to a temp location first to avoid locking issues with an open connection.
                var tempCopy = Path.GetTempFileName();
                File.Copy(dbPath, tempCopy, overwrite: true);
                zip.CreateEntryFromFile(tempCopy, "database/markdown-hub.db");
                File.Delete(tempCopy);
            }

            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(appsettingsPath))
            {
                zip.CreateEntryFromFile(appsettingsPath, "config/appsettings.json");
            }
        }

        var info = new FileInfo(fullPath);
        var record = new BackupRecord { FileName = fileName, SizeBytes = info.Length, TriggeredManually = manual };
        _db.Backups.Add(record);
        await _db.SaveChangesAsync(ct);

        await EnforceRetentionAsync(backupDir, ct);
        _logger.LogInformation("Backup created: {FileName} ({Size} bytes)", fileName, info.Length);
        return record;
    }

    private async Task EnforceRetentionAsync(string backupDir, CancellationToken ct)
    {
        var retain = _config.GetValue<int?>("Hub:BackupsToRetain") ?? 14;
        // SQLite can't translate ORDER BY over a DateTimeOffset column - fetch then sort
        // client-side (same limitation/workaround used elsewhere in this codebase, e.g.
        // AuditLogService).
        var all = (await _db.Backups.ToListAsync(ct)).OrderByDescending(b => b.CreatedAt).ToList();
        foreach (var stale in all.Skip(retain))
        {
            var path = Path.Combine(backupDir, stale.FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _db.Backups.Remove(stale);
        }
        await _db.SaveChangesAsync(ct);
    }

    private string ExtractSqlitePath()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        var part = cs.Split(';').FirstOrDefault(p => p.Trim().StartsWith("Data Source", StringComparison.OrdinalIgnoreCase));
        return part?.Split('=', 2).ElementAtOrDefault(1)?.Trim() ?? "/data/db/markdown-hub.db";
    }

    private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPrefix)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, $"{entryPrefix}/{relative}");
        }
    }
}
