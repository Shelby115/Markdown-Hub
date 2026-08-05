using System.Text.RegularExpressions;

namespace MarkdownHub.Api.Services;

public enum WikiLinkKind { Link, Embed }

public record ParsedWikiLink(
    WikiLinkKind Kind,
    string Target,      // e.g. "Folder/PageName"
    string? Anchor,      // e.g. "Section Heading"
    string? DisplayText, // explicit |LinkText override, if any
    string RawMatch
);

/// <summary>
/// Parses wiki-style [[PageName]], [[PageName|Text]], [[Folder/Page#Anchor]]
/// and ![[Embed]] / ![[Embed#Anchor]] syntax out of raw Markdown text.
/// Pure parsing only - resolution against the page index happens elsewhere.
/// </summary>
public static partial class WikiLinkParser
{
    // ![[Target#Anchor]]  or  [[Target#Anchor|Display]]
    [GeneratedRegex(@"(?<embed>!)?\[\[(?<target>[^\]\|#]+?)(?:#(?<anchor>[^\]\|]+))?(?:\|(?<display>[^\]]+))?\]\]")]
    private static partial Regex LinkPattern();

    public static IReadOnlyList<ParsedWikiLink> Parse(string markdown)
    {
        var results = new List<ParsedWikiLink>();
        foreach (Match m in LinkPattern().Matches(markdown))
        {
            var kind = m.Groups["embed"].Success ? WikiLinkKind.Embed : WikiLinkKind.Link;
            var target = m.Groups["target"].Value.Trim();
            var anchor = m.Groups["anchor"].Success ? m.Groups["anchor"].Value.Trim() : null;
            var display = m.Groups["display"].Success ? m.Groups["display"].Value.Trim() : null;
            results.Add(new ParsedWikiLink(kind, target, anchor, display, m.Value));
        }
        return results;
    }

    /// <summary>Detects a recursive embed cycle before resolving ![[...]] content.</summary>
    public static bool WouldCreateCycle(string sourcePage, string targetPage, IReadOnlyDictionary<string, HashSet<string>> embedGraph)
    {
        // BFS from targetPage looking for sourcePage - if found, embedding target from source is a cycle.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(targetPage);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (string.Equals(current, sourcePage, StringComparison.OrdinalIgnoreCase)) return true;

            if (embedGraph.TryGetValue(current, out var children))
                foreach (var child in children)
                    queue.Enqueue(child);
        }
        return false;
    }
}
