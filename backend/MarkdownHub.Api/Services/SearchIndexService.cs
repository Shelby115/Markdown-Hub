using Microsoft.Data.Sqlite;

namespace MarkdownHub.Api.Services;

public record SearchHit(string RelativePath, string PageName, string Snippet);

/// <summary>
/// Full-text search over page names, folder names, and Markdown content using a
/// SQLite FTS5 virtual table. This is intentionally a separate physical table from
/// PageMetadata (EF-mapped) since FTS5 virtual tables aren't first-class EF citizens -
/// we manage it with raw SQL, keyed by the same relative path.
/// </summary>
public class SearchIndexService
{
    private readonly string _connectionString;

    public SearchIndexService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")!;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS PageSearchIndex USING fts5(
                relative_path UNINDEXED,
                page_name,
                folder_name,
                content,
                tokenize = 'porter unicode61'
            );
        """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAsync(string relativePath, string pageName, string folderName, string plainTextContent, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM PageSearchIndex WHERE relative_path = $path;";
            del.Parameters.AddWithValue("$path", relativePath);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO PageSearchIndex (relative_path, page_name, folder_name, content)
            VALUES ($path, $name, $folder, $content);
        """;
        ins.Parameters.AddWithValue("$path", relativePath);
        ins.Parameters.AddWithValue("$name", pageName);
        ins.Parameters.AddWithValue("$folder", folderName);
        ins.Parameters.AddWithValue("$content", plainTextContent);
        await ins.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(string relativePath, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PageSearchIndex WHERE relative_path = $path;";
        cmd.Parameters.AddWithValue("$path", relativePath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Runs the FTS query and returns raw hits. Caller is responsible for filtering
    /// results down to paths the requesting user has permission to view.
    /// </summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT relative_path, page_name, snippet(PageSearchIndex, 3, '<mark>', '</mark>', '…', 12)
            FROM PageSearchIndex
            WHERE PageSearchIndex MATCH $query
            ORDER BY rank
            LIMIT $limit;
        """;
        // Wrap each term in quotes + a trailing * for prefix matching, e.g. "proj*"
        var ftsQuery = string.Join(" ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => $"\"{t.Replace("\"", "")}\"*"));
        cmd.Parameters.AddWithValue("$query", ftsQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SearchHit(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return results;
    }

    public async Task RebuildFromFilesystemAsync(HubPathService hub, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var clear = conn.CreateCommand())
        {
            clear.CommandText = "DELETE FROM PageSearchIndex;";
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var file in Directory.EnumerateFiles(hub.Root, "*.md", SearchOption.AllDirectories))
        {
            var relative = hub.ToRelative(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var folder = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            var content = await File.ReadAllTextAsync(file, ct);
            await UpsertAsync(relative, name, folder, content, ct);
        }
    }
}
