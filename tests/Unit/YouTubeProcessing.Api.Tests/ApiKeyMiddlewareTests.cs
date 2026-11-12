using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using YouTubeProcessing.Api.Infrastructure;

namespace YouTubeProcessing.Api.Tests;

public sealed class ApiKeyMiddlewareTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task PublicPathsDoNotRequireAnApiKey(string path)
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

    [Fact]
    public async Task ProtectedPathsRejectMissingApiKey()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/jobs/00000000-0000-0000-0000-000000000000";

        await middleware.InvokeAsync(
            context,
            Options.Create(new PlatformOptions { ApiKey = "test-key" }));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProtectedPathsAcceptValidApiKey()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/video-analyses";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "test-key";

        await middleware.InvokeAsync(
            context,
            Options.Create(new PlatformOptions { ApiKey = "test-key" }));

        Assert.True(nextCalled);
    }
}
