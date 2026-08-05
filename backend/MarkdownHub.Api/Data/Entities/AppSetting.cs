namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// App-wide key/value settings an admin can change at runtime without a redeploy - e.g. which
/// Ollama model to use. Deliberately generic (not a dedicated "AiSettings" table) so future
/// runtime-configurable settings can reuse it instead of each needing their own table/column.
/// </summary>
public class AppSetting
{
    public const string AiOllamaModelKey = "Ai.Ollama.Model";

    public int Id { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }
}
