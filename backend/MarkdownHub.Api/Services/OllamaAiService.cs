using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Talks to a local Ollama instance's /api/chat and /api/tags endpoints. Never called directly
/// by controllers - only through IAiService, so the rest of the app has no Ollama-specific
/// knowledge. Configuration lives under "Ai:Ollama" (BaseUrl, Model, TimeoutSeconds); see
/// appsettings.json. The model itself can additionally be overridden at runtime by an admin via
/// the AppSettings table (AiSettingsController) - that override, when present, always wins over
/// the configured default, so changing it in the UI never needs a redeploy or restart.
/// </summary>
public class OllamaAiService : IAiService
{
    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly string _baseUrl;
    private readonly string _configuredDefaultModel;

    public OllamaAiService(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext db)
    {
        _http = httpClientFactory.CreateClient(nameof(OllamaAiService));
        _db = db;
        _baseUrl = (config["Ai:Ollama:BaseUrl"] ?? "http://host.docker.internal:11434").TrimEnd('/');
        _configuredDefaultModel = config["Ai:Ollama:Model"] ?? "gpt-oss:20b";
        var timeoutSeconds = config.GetValue<int?>("Ai:Ollama:TimeoutSeconds") ?? 60;
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var model = await ResolveModelAsync(ct);
        var request = new OllamaChatRequest(
            model,
            [new OllamaChatMessage("system", systemPrompt), new OllamaChatMessage("user", userPrompt)],
            stream: false
        );

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync($"{_baseUrl}/api/chat", request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiServiceException("The AI model took too long to respond. Try a shorter selection, or check that Ollama is running.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceException("Couldn't reach the AI service. Check that Ollama is running and reachable from the server.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AiServiceException($"The AI service returned an error (HTTP {(int)response.StatusCode}): {Truncate(body, 300)}");
        }

        OllamaChatResponse? parsed;
        try
        {
            parsed = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(ct);
        }
        catch (Exception ex)
        {
            throw new AiServiceException("The AI service returned a response that couldn't be understood.", ex);
        }

        var content = parsed?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new AiServiceException("The AI service returned an empty response.");

        return content.Trim();
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"{_baseUrl}/api/tags", ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiServiceException("Timed out asking Ollama for its installed models.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceException("Couldn't reach the AI service. Check that Ollama is running and reachable from the server.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AiServiceException($"The AI service returned an error (HTTP {(int)response.StatusCode}): {Truncate(body, 300)}");
        }

        OllamaTagsResponse? parsed;
        try
        {
            parsed = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(ct);
        }
        catch (Exception ex)
        {
            throw new AiServiceException("The AI service returned a response that couldn't be understood.", ex);
        }

        return parsed?.Models?.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
    }

    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == AppSetting.AiOllamaModelKey, ct);
        return !string.IsNullOrWhiteSpace(setting?.Value) ? setting.Value : _configuredDefaultModel;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

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

public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }
}

public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo>? Models { get; set; }
}

public class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
