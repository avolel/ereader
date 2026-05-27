using EReader.Core.Services;
using FluentAssertions;

namespace EReader.Tests.Services;

public class AssetUrlRewriterTests
{
    private const string Base = "/api/v1/books/abc/assets";

    [Fact]
    public void Should_RewriteRelativeImageSrc_When_PointingUpADirectory()
    {
        var content = """<img src="../images/foo.png"/>""";

        var result = AssetUrlRewriter.Rewrite(content, "OEBPS/text/ch1.xhtml", Base);

        result.Should().Be($"""<img src="{Base}/OEBPS/images/foo.png"/>""");
    }

    [Fact]
    public void Should_RewriteRelativeStylesheetHref_When_InSameDirectory()
    {
        var content = """<link rel="stylesheet" href="style.css"/>""";

        var result = AssetUrlRewriter.Rewrite(content, "OEBPS/text/ch1.xhtml", Base);

        result.Should().Contain($"href=\"{Base}/OEBPS/text/style.css\"");
    }

    [Fact]
    public void Should_LeaveAbsoluteUrlsUntouched_When_RewritingContent()
    {
        var content = """<a href="https://example.com/x">x</a><img src="//cdn/x.png"/><a href="/api/foo">y</a>""";

        var result = AssetUrlRewriter.Rewrite(content, "OEBPS/text/ch1.xhtml", Base);

        result.Should().Be(content);
    }

    [Fact]
    public void Should_LeaveFragmentOnlyAndDataUrisUntouched_When_RewritingContent()
    {
        var content = """<a href="#section1">x</a><img src="data:image/png;base64,abc"/>""";

        var result = AssetUrlRewriter.Rewrite(content, "OEBPS/text/ch1.xhtml", Base);

        result.Should().Be(content);
    }

    [Fact]
    public void Should_PreserveFragmentInUrl_When_RelativeHrefHasAnchor()
    {
        var content = """<a href="other.xhtml#h2">x</a>""";

        var result = AssetUrlRewriter.Rewrite(content, "OEBPS/text/ch1.xhtml", Base);

        result.Should().Contain($"href=\"{Base}/OEBPS/text/other.xhtml#h2\"");
    }

    [Fact]
    public void Should_HandleEmptyContent_When_PassedEmpty()
    {
        AssetUrlRewriter.Rewrite(string.Empty, "OEBPS/ch.xhtml", Base).Should().Be(string.Empty);
    }
}
