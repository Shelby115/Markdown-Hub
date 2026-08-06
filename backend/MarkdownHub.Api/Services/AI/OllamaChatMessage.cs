using System.Text.Json.Serialization;

namespace MarkdownHub.Api.Services;

public class OllamaChatMessage
{
    public OllamaChatMessage() { }
    public OllamaChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
