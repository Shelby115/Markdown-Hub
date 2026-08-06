namespace MarkdownHub.Api.Services;

public class AiServiceException : Exception
{
    public AiServiceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Provider-independent abstraction for a single-shot chat completion. The rest of the
/// application (AiController, the knowledge assistant) should depend on this, never on an
/// Ollama-specific type, so a different provider can be swapped in later without touching them.
/// </summary>
public interface IAiService
{
    /// <summary>Sends one system+user prompt pair and returns the model's reply as plain text.
    /// Throws AiServiceException (with a message safe to show a user) on any provider failure -
    /// connection refused, timeout, non-success response, malformed response, etc.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Lists model names currently available from the provider (e.g. Ollama models
    /// already pulled locally), for an admin settings UI to pick from. Throws
    /// AiServiceException on failure, same as CompleteAsync.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
