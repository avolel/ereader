using System.Net;
using EReader.Core.Exceptions;
using EReader.Core.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace EReader.Tests.Services;

public class WikipediaServiceTests
{
    // Canned REST summary payload, trimmed to the fields WikipediaService reads.
    private const string SummaryJson = """
    {
      "title": "Project Gutenberg",
      "extract": "Project Gutenberg is a volunteer effort to digitize books.",
      "content_urls": { "desktop": { "page": "https://en.wikipedia.org/wiki/Project_Gutenberg" } },
      "thumbnail": { "source": "https://upload.wikimedia.org/thumb.png" }
    }
    """;

    // Builds a WikipediaService whose HttpClient is backed by a stubbed handler returning
    // the given response — no network. BaseAddress is required because the service issues a
    // relative request ("page/summary/{title}").
    private static (WikipediaService service, Mock<HttpMessageHandler> handler) BuildService(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://en.wikipedia.org/api/rest_v1/") };
        return (new WikipediaService(http), handler);
    }

    [Fact]
    public async Task Should_ReturnSummary_When_ApiReturns200()
    {
        var (service, _) = BuildService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SummaryJson),
        });

        var result = await service.GetSummaryAsync("Project Gutenberg", CancellationToken.None);

        result.Found.Should().BeTrue();
        result.Term.Should().Be("Project Gutenberg");
        result.Title.Should().Be("Project Gutenberg");
        result.Extract.Should().Be("Project Gutenberg is a volunteer effort to digitize books.");
        result.PageUrl.Should().Be("https://en.wikipedia.org/wiki/Project_Gutenberg");
        result.ThumbnailUrl.Should().Be("https://upload.wikimedia.org/thumb.png");
    }

    [Fact]
    public async Task Should_ReturnFoundFalse_When_ApiReturns404()
    {
        var (service, _) = BuildService(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.GetSummaryAsync("asdkjfhqwoeiu", CancellationToken.None);

        result.Found.Should().BeFalse();
        result.Title.Should().BeNull();
        result.Extract.Should().BeNull();
        result.PageUrl.Should().BeNull();
        result.ThumbnailUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_Throw_When_TermEmpty(string term)
    {
        // Strict mock with no SendAsync setup: if the service hit the network the test would
        // fail, proving validation short-circuits before any request.
        var (service, handler) = BuildService(new HttpResponseMessage(HttpStatusCode.OK));

        var act = async () => await service.GetSummaryAsync(term, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        handler.Protected().Verify(
            "SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}