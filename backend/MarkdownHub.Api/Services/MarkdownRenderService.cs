using Ganss.Xss;
using Markdig;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Converts hub Markdown (including [[wiki links]]) to sanitized HTML.
/// Wiki links are rewritten to internal anchors BEFORE Markdig runs, then the
/// final HTML is passed through an HTML sanitizer allow-list before it is ever
/// sent to a browser - this is the single choke point for XSS prevention.
/// </summary>
public class MarkdownRenderService
{
    private readonly MarkdownPipeline _pipeline;
    private readonly HtmlSanitizer _sanitizer;

    public MarkdownRenderService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions() // tables, autolinks, footnotes, task lists, etc.
            .Build();

        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.Add("input"); // for task-list checkboxes
        _sanitizer.AllowedTags.Add("audio");
        _sanitizer.AllowedTags.Add("video");
        _sanitizer.AllowedAttributes.Add("type");
        _sanitizer.AllowedAttributes.Add("checked");
        _sanitizer.AllowedAttributes.Add("disabled");
        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("data-page-exists");
        _sanitizer.AllowedAttributes.Add("data-page");
        _sanitizer.AllowedAttributes.Add("controls");
    }

    /// <param name="markdown">Raw file content.</param>
    /// <param name="resolveLinkHref">Given a wiki-link target, returns (href, exists).</param>
    /// <param name="resolveEmbedSrc">Given an image/audio/video/PDF embed target, returns a src
    /// URL the viewer can actually load it from, or null if it can't be resolved (falls back to
    /// a plain text placeholder in that case). Not invoked for a bare note-transclusion target
    /// (no recognized media extension) - those always render as a placeholder here, since
    /// recursively rendering another page's content server-side isn't implemented; the live
    /// editor's own transclusion widget handles that case by fetching the target page directly.</param>
    public string RenderToSafeHtml(
        string markdown,
        Func<string, (string Href, bool Exists)> resolveLinkHref,
        Func<string, string?>? resolveEmbedSrc = null)
    {
        var withResolvedLinks = RewriteWikiLinks(markdown, resolveLinkHref, resolveEmbedSrc);
        var html = Markdown.ToHtml(withResolvedLinks, _pipeline);
        return _sanitizer.Sanitize(html);
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"
    };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac"
    };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".ogv", ".mov", ".mkv"
    };

    private static string RewriteWikiLinks(
        string markdown,
        Func<string, (string Href, bool Exists)> resolve,
        Func<string, string?>? resolveEmbedSrc)
    {
        var links = WikiLinkParser.Parse(markdown);

        // Global replace per unique raw match is safe here: hub pages are single-digit-MB
        // at most, and RawMatch strings (e.g. "[[Page|Text]]") are specific enough not to
        // collide with unrelated text elsewhere in the document.
        var result = markdown;
        foreach (var link in links.DistinctBy(l => l.RawMatch))
        {
            string replacement;
            if (link.Kind == WikiLinkKind.Embed)
            {
                // CommonMark's HTML-block rules kick in for *any* complete HTML tag that sits
                // alone on its own line - not just block tags like <div> - and once triggered,
                // that block swallows every following line verbatim until the next blank line.
                // Since an embed normally occupies its whole source line by itself, that silently
                // broke every list/heading/table that followed one with no blank line separating
                // them (a real bug, hit in practice - not hypothetical). Surrounding the
                // replacement with blank lines unconditionally guarantees the HTML block (if one
                // even triggers) terminates immediately after it; extra blank lines are otherwise
                // harmless since CommonMark collapses any run of them into a single separator.
                var ext = Path.GetExtension(link.Target);
                var isImage = ImageExtensions.Contains(ext);
                var isAudio = AudioExtensions.Contains(ext);
                var isVideo = VideoExtensions.Contains(ext);
                var isPdf = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                var src = (isImage || isAudio || isVideo || isPdf) ? resolveEmbedSrc?.Invoke(link.Target) : null;
                var encodedTarget = System.Net.WebUtility.HtmlEncode(link.Target);
                string tag;
                if (src is null)
                {
                    var icon = isImage ? "🖼" : isAudio ? "🔊" : isVideo ? "🎬" : isPdf ? "📄" : "📄";
                    tag = $"<span class=\"wiki-embed-placeholder\">{icon} {encodedTarget}</span>";
                }
                else
                {
                    var encodedSrc = System.Net.WebUtility.HtmlEncode(src);
                    tag = isImage
                        ? $"<img class=\"wiki-embed-image\" src=\"{encodedSrc}\" alt=\"{encodedTarget}\">"
                        : isAudio
                        ? $"<audio class=\"wiki-embed-audio\" controls src=\"{encodedSrc}\">{encodedTarget}</audio>"
                        : isVideo
                        ? $"<video class=\"wiki-embed-video\" controls src=\"{encodedSrc}\">{encodedTarget}</video>"
                        // PDFs render as a plain link here rather than an inline viewer - keeps
                        // the sanitizer's allowed-tag surface free of iframe/embed/object, which
                        // (unlike img/audio/video) can render arbitrary same-styled foreign
                        // content. The live editor gives PDFs a real inline preview instead,
                        // built as a DOM widget that never passes through this sanitized path.
                        : $"<a class=\"wiki-embed-pdf-link\" href=\"{encodedSrc}\" target=\"_blank\">📄 {encodedTarget} ↗</a>";
                }
                replacement = $"\n\n{tag}\n\n";
            }
            else
            {
                var (href, exists) = resolve(link.Target);
                var text = link.DisplayText ?? link.Target.Split('/').Last();
                var anchorSuffix = link.Anchor is null ? "" : $"#{Uri.EscapeDataString(link.Anchor)}";
                var cssClass = exists ? "wiki-link" : "wiki-link wiki-link-missing";
                replacement = $"<a href=\"{System.Net.WebUtility.HtmlEncode(href + anchorSuffix)}\" " +
                               $"class=\"{cssClass}\" data-page-exists=\"{exists}\">{System.Net.WebUtility.HtmlEncode(text)}</a>";
            }
            result = result.Replace(link.RawMatch, replacement);
        }
        return result;
    }
}
