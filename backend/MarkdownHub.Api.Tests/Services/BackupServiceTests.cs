using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class BackupServiceTests : IDisposable
{
    private readonly string _hubRoot;
    private readonly string _backupDir;
    private readonly AppDbContext _db;
    private readonly BackupService _sut;

    public BackupServiceTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
        _backupDir = Directory.CreateTempSubdirectory("markdown-hub-tests-backup-").FullName;

        // A root-level attachment (edge case) and a per-folder attachment (the shape
        // AttachmentsController.Upload actually produces for every real upload).
        File.WriteAllText(Path.Combine(_hubRoot, "notes.md"), "# Notes");
        Directory.CreateDirectory(Path.Combine(_hubRoot, ".attachments"));
        File.WriteAllBytes(Path.Combine(_hubRoot, ".attachments", "root-image.png"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(_hubRoot, "SubFolder", ".attachments"));
        File.WriteAllText(Path.Combine(_hubRoot, "SubFolder", "notes2.md"), "# More notes");
        File.WriteAllBytes(Path.Combine(_hubRoot, "SubFolder", ".attachments", "nested-image.png"), [4, 5, 6]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:MarkdownRoot"] = _hubRoot,
                ["Hub:BackupLocation"] = _backupDir,
            })
            .Build();
        var hub = new HubPathService(config);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _sut = new BackupService(hub, config, _db, NullLogger<BackupService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_backupDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RunBackupAsync_IncludesEveryAttachmentExactlyOnce()
    {
        var record = await _sut.RunBackupAsync(manual: true);

        using var zip = ZipFile.OpenRead(Path.Combine(_backupDir, record.FileName));
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        // Every attachment (root-level and per-folder) must appear exactly once, under the
        // "markdown/" prefix where AddDirectoryToZip's single recursive pass already puts it -
        // not duplicated under a second "attachments/" prefix, and not missed for nested folders.
        Assert.Equal(1, entryNames.Count(n => n.EndsWith("root-image.png")));
        Assert.Equal(1, entryNames.Count(n => n.EndsWith("nested-image.png")));
        Assert.Contains("markdown/.attachments/root-image.png", entryNames);
        Assert.Contains("markdown/SubFolder/.attachments/nested-image.png", entryNames);

        // No separate top-level "attachments/" prefix should exist at all.
        Assert.DoesNotContain(entryNames, n => n.StartsWith("attachments/"));
    }
}
