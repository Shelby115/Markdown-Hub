using System.Text.Json.Serialization;

namespace MarkdownHub.Api.Services;

public class OllamaChatRequest
{
    public OllamaChatRequest(string model, List<OllamaChatMessage> messages, bool stream)
    {
        Model = model;
        Messages = messages;
        Stream = stream;
    }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
