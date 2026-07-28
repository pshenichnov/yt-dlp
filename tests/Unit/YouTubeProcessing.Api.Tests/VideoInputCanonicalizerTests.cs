using YouTubeProcessing.Api.Infrastructure;

namespace YouTubeProcessing.Api.Tests;

public sealed class VideoInputCanonicalizerTests
{
    [Theory]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    public void CanonicalizesSupportedInputs(string input)
    {
        var valid = VideoInputCanonicalizer.TryCanonicalize(input, 2048, out var videoId);

        Assert.True(valid);
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-video")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    public void RejectsUnsupportedInputs(string input)
    {
        Assert.False(VideoInputCanonicalizer.TryCanonicalize(input, 2048, out _));
    }
}
