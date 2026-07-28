using System.Text.Json;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Orchestrator.Tests;

public sealed class ExtractorContractTests
{
    [Fact]
    public void RawCommandUsesCamelCaseVersionedEnvelope()
    {
        var jobId = Guid.NewGuid();
        var message = new IntegrationEnvelopeV1<ExtractVideoPayloadV1>(
            "1",
            Guid.NewGuid(),
            jobId,
            Guid.NewGuid(),
            IntegrationMessageTypes.ExtractVideo,
            DateTimeOffset.UtcNow,
            new ExtractVideoPayloadV1(
                jobId,
                Guid.NewGuid(),
                "dQw4w9WgXcQ",
                "https://youtu.be/dQw4w9WgXcQ",
                "en"));

        var json = JsonSerializer.Serialize(
            message,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "extract-video",
            document.RootElement.GetProperty("messageType").GetString());
        Assert.Equal(
            "dQw4w9WgXcQ",
            document.RootElement.GetProperty("payload").GetProperty("videoId").GetString());
    }
}
