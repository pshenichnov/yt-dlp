using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using YouTubeProcessing.Api.Infrastructure;

namespace YouTubeProcessing.Api.Tests;

public sealed class ApiKeyMiddlewareTests
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task DocumentationPathsDoNotRequireAnApiKey(string path)
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(
            context,
            Options.Create(new PlatformOptions { ApiKey = "test-key" }));

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
