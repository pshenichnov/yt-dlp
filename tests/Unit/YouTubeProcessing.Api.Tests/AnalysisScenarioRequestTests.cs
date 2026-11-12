using System.Text.Json;
using YouTubeProcessing.Api.Endpoints;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Api.Tests;

public sealed class AnalysisScenarioRequestTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void DefaultsToSummaryAndTopicsWhenScenariosAreOmitted()
    {
        var request = JsonSerializer.Deserialize<CreateVideoAnalysisRequest>(
            """
            {"video":"dQw4w9WgXcQ"}
            """,
            WebJson);

        Assert.NotNull(request);
        Assert.Equal(AnalysisScenarioDefaults.All, request.Scenarios);
    }

    [Fact]
    public void AcceptsMultipleStringEnumScenarios()
    {
        var request = JsonSerializer.Deserialize<CreateVideoAnalysisRequest>(
            """
            {
              "video":"dQw4w9WgXcQ",
              "scenarios":["topics","summary"]
            }
            """,
            WebJson);

        Assert.NotNull(request);
        Assert.Equal(
            [AnalysisScenarioV1.Topics, AnalysisScenarioV1.Summary],
            request.Scenarios);
    }

    [Fact]
    public void RejectsNumericScenarioValues()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateVideoAnalysisRequest>(
                """
                {"video":"dQw4w9WgXcQ","scenarios":[99]}
                """,
                WebJson));
    }
}
