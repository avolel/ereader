using EReader.Api.Auth;
using FluentAssertions;

namespace EReader.Tests.Auth;

public class MediaQueryTokenTests
{
    private const string Guid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Should_AuthorizeAsset_When_TokenInQueryString()
    {
        var token = MediaQueryToken.ResolveQueryToken(
            "GET", $"/api/v1/books/{Guid}/assets/OEBPS/images/cover.png", "tok-123");

        token.Should().Be("tok-123");
    }

    [Fact]
    public void Should_AuthorizeCover_When_TokenInQueryString()
    {
        var token = MediaQueryToken.ResolveQueryToken(
            "GET", $"/api/v1/books/{Guid}/cover", "tok-123");

        token.Should().Be("tok-123");
    }

    [Fact]
    public void Should_IgnoreQueryToken_When_RouteIsNotMedia()
    {
        var token = MediaQueryToken.ResolveQueryToken(
            "GET", $"/api/v1/books/{Guid}/chapters/{Guid}", "tok-123");

        token.Should().BeNull();
    }

    [Fact]
    public void Should_IgnoreQueryToken_When_MethodIsNotGet()
    {
        var token = MediaQueryToken.ResolveQueryToken(
            "POST", $"/api/v1/books/{Guid}/assets/x.png", "tok-123");

        token.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnNull_When_NoQueryTokenPresent()
    {
        var token = MediaQueryToken.ResolveQueryToken(
            "GET", $"/api/v1/books/{Guid}/cover", null);

        token.Should().BeNull();
    }

    [Theory]
    [InlineData("GET", "/api/v1/books/abc/cover", true)]
    [InlineData("GET", "/api/v1/books/abc/assets", true)]
    [InlineData("GET", "/api/v1/books/abc/assets/OEBPS/img/1.png", true)]
    [InlineData("GET", "/api/v1/books/abc/chapters/def", false)]
    [InlineData("GET", "/api/v1/auth/refresh", false)]
    [InlineData("POST", "/api/v1/books/abc/cover", false)]
    public void Should_MatchOnlyMediaGetRoutes(string method, string path, bool expected)
    {
        MediaQueryToken.IsMediaRoute(method, path).Should().Be(expected);
    }
}
