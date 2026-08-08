namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// A named library of pre-generated content for one kind of AI Template placeholder (e.g.
/// "Interactible"). A template opts in with a "- Pool: Interactible" line in its ai-template
/// block; from then on that placeholder is served from this pool instead of waiting on the model.
/// </summary>
public class GenerationPool
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>The generation rules, in the same bullet syntax a template's ai-template block
    /// uses, so both are authored and parsed the same way.</summary>
    public string Instructions { get; set; } = "";

    /// <summary>How many Ready entries the background generator keeps on hand. Also the cap -
    /// it stops generating once the pool is full.</summary>
    public int TargetCount { get; set; } = 20;

    /// <summary>Whether the background generator may fill this pool. Off by default: nothing runs
    /// against Ollama until an admin asks for it.</summary>
    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
