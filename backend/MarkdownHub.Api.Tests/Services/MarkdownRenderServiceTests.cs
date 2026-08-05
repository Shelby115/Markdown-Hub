using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class MarkdownRenderServiceTests
{
    private readonly MarkdownRenderService _sut = new();

    private static (string Href, bool Exists) NoLinks(string target) => ("#", false);

    [Fact]
    public void Embed_ImageWithResolvedSrc_RendersImgTag()
    {
        var html = _sut.RenderToSafeHtml("![[photo.png]]", NoLinks, _ => "/api/attachments/photo.png");

        Assert.Contains("<img", html);
        Assert.Contains("src=\"/api/attachments/photo.png\"", html);
        Assert.Contains("wiki-embed-image", html);
    }

    [Fact]
    public void Embed_AudioWithResolvedSrc_RendersAudioTag()
    {
        var html = _sut.RenderToSafeHtml("![[song.mp3]]", NoLinks, _ => "/api/attachments/song.mp3");

        Assert.Contains("<audio", html);
        Assert.Contains("controls", html);
        Assert.Contains("src=\"/api/attachments/song.mp3\"", html);
        Assert.Contains("wiki-embed-audio", html);
    }

    [Fact]
    public void Embed_VideoWithResolvedSrc_RendersVideoTag()
    {
        var html = _sut.RenderToSafeHtml("![[clip.mp4]]", NoLinks, _ => "/api/attachments/clip.mp4");

        Assert.Contains("<video", html);
        Assert.Contains("controls", html);
        Assert.Contains("src=\"/api/attachments/clip.mp4\"", html);
        Assert.Contains("wiki-embed-video", html);
    }

    [Fact]
    public void Embed_PdfWithResolvedSrc_RendersLinkNotIframe()
    {
        var html = _sut.RenderToSafeHtml("![[Handbook.pdf]]", NoLinks, _ => "/api/attachments/Handbook.pdf");

        Assert.Contains("<a", html);
        Assert.Contains("href=\"/api/attachments/Handbook.pdf\"", html);
        Assert.Contains("wiki-embed-pdf-link", html);
        // The sanitizer's allowed-tag surface deliberately stays free of iframe/embed/object -
        // PDFs render as a plain link server-side (the live editor gives them a real inline
        // preview through its own DOM widget instead, entirely separate from this HTML).
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<embed", html);
    }

    [Fact]
    public void Embed_UnresolvedTarget_RendersPlaceholderSpan_NoMatterTheExtension()
    {
        var html = _sut.RenderToSafeHtml("![[missing.mp3]]", NoLinks, _ => null);

        Assert.Contains("wiki-embed-placeholder", html);
        Assert.Contains("missing.mp3", html);
        Assert.DoesNotContain("<audio", html);
    }

    [Fact]
    public void Embed_BareNoteTransclusionTarget_NeverInvokesResolveEmbedSrc()
    {
        var invoked = false;
        _sut.RenderToSafeHtml("![[Some Page]]", NoLinks, target =>
        {
            invoked = true;
            return "/should-not-be-used";
        });

        Assert.False(invoked);
    }

    [Fact]
    public void Embed_BareNoteTransclusionTarget_RendersPlaceholderSpan()
    {
        var html = _sut.RenderToSafeHtml("![[Some Page]]", NoLinks);

        Assert.Contains("wiki-embed-placeholder", html);
        Assert.Contains("Some Page", html);
    }

    [Fact]
    public void RawScriptTagInMarkdown_IsStrippedBySanitizer()
    {
        var html = _sut.RenderToSafeHtml("<script>alert(1)</script>", NoLinks);

        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void RawIframeTagInMarkdown_IsStrippedBySanitizer_EvenThoughAudioVideoAreAllowed()
    {
        var html = _sut.RenderToSafeHtml("<iframe src=\"https://example.com\"></iframe>", NoLinks);

        Assert.DoesNotContain("<iframe", html);
    }
}
