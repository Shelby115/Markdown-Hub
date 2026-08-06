using System.Text.Json.Serialization;

namespace MarkdownHub.Api.Services;

public class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
