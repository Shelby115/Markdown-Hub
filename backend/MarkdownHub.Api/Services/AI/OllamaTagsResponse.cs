using System.Text.Json.Serialization;

namespace MarkdownHub.Api.Services;

public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo>? Models { get; set; }
}
