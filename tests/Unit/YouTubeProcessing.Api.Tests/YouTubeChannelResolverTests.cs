using System.Net;
using YouTubeProcessing.Api.Infrastructure;

namespace YouTubeProcessing.Api.Tests;

public sealed class YouTubeChannelResolverTests
{
    private const string ChannelId = "UCVedwIvkmSZdYUcy5OJJRZQ";

    [Theory]
    [InlineData("UCVedwIvkmSZdYUcy5OJJRZQ")]
    [InlineData("https://www.youtube.com/channel/UCVedwIvkmSZdYUcy5OJJRZQ")]
    [InlineData("https://www.youtube.com/channel/UCVedwIvkmSZdYUcy5OJJRZQ/videos")]
    public async Task ReturnsCanonicalChannelIdWithoutNetworkRequest(string input)
    {
        var handler = new StubHttpMessageHandler((_) => throw new InvalidOperationException());
        var resolver = new YouTubeChannelResolver(new HttpClient(handler));

        var result = await resolver.ResolveChannelId(input, CancellationToken.None);

        Assert.Equal(ChannelId, result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("@alexsokolovskiy")]
    [InlineData("https://www.youtube.com/@alexsokolovskiy")]
    [InlineData("https://youtube.com/@alexsokolovskiy/videos")]
    public async Task ResolvesHandleFromYouTubeChannelPage(string input)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("https://www.youtube.com/@alexsokolovskiy", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"<script>{{\"metadata\":{{\"externalId\":\"{ChannelId}\"}}}}</script>")
            };
        });
        var resolver = new YouTubeChannelResolver(new HttpClient(handler));

        var result = await resolver.ResolveChannelId(input, CancellationToken.None);

        Assert.Equal(ChannelId, result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alexsokolovskiy")]
    [InlineData("http://www.youtube.com/@alexsokolovskiy")]
    [InlineData("https://example.com/@alexsokolovskiy")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    public async Task RejectsUnsupportedInput(string input)
    {
        var handler = new StubHttpMessageHandler((_) => throw new InvalidOperationException());
        var resolver = new YouTubeChannelResolver(new HttpClient(handler));

        var result = await resolver.ResolveChannelId(input, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ReturnsNullWhenHandlePageDoesNotContainAChannelId()
    {
        var handler = new StubHttpMessageHandler((_) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>channel unavailable</html>")
        });
        var resolver = new YouTubeChannelResolver(new HttpClient(handler));

        var result = await resolver.ResolveChannelId(
            "https://www.youtube.com/@missing-channel",
            CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
