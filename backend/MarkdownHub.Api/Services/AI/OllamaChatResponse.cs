using System.Text.Json.Serialization;

namespace MarkdownHub.Api.Services;

public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }
}
