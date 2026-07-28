using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Orchestrator.Tests;

public sealed class ExtractionCompletionPlanTests
{
    [Fact]
    public void DisabledAnalysisCompletesWithExtractionArtifacts()
    {
        var extraction = CreateExtraction();
        var occurredAt = DateTimeOffset.UtcNow;

        var plan = ExtractionCompletionPlan.Create(extraction, false, occurredAt);

        Assert.Equal(JobStages.Completed, plan.NextStage);
        Assert.Null(plan.StageChange);
        Assert.Null(plan.AnalysisRequest);
        Assert.NotNull(plan.Completion);
        Assert.Equal(extraction.Transcript, plan.Completion.Transcript);
        Assert.Equal(extraction.Thumbnail, plan.Completion.Thumbnail);
        Assert.Empty(plan.Completion.Summary);
        Assert.Empty(plan.Completion.Subjects);
        Assert.Empty(plan.Completion.Model);
    }

    [Fact]
    public void EnabledAnalysisPreservesAnalyzerWorkflow()
    {
        var extraction = CreateExtraction();
        var occurredAt = DateTimeOffset.UtcNow;

        var plan = ExtractionCompletionPlan.Create(extraction, true, occurredAt);

        Assert.Equal(JobStages.Analyzing, plan.NextStage);
        Assert.Equal(JobStages.Analyzing, plan.StageChange?.Stage);
        Assert.NotNull(plan.AnalysisRequest);
        Assert.Null(plan.Completion);
        Assert.Equal(extraction.Transcript, plan.AnalysisRequest.Transcript);
        Assert.Equal(extraction.Thumbnail, plan.AnalysisRequest.Thumbnail);
    }

    private static ExtractVideoCompletedPayloadV1 CreateExtraction()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        return new ExtractVideoCompletedPayloadV1(
            jobId,
            analysisId,
            new VideoMetadataV1(
                "dQw4w9WgXcQ",
                "Test video",
                null,
                null,
                null,
                60,
                null,
                null,
                null,
                "https://www.youtube.com/watch?v=dQw4w9WgXcQ"),
            CreateArtifact("transcript", "transcript.txt", "text/plain"),
            null,
            null,
            CreateArtifact("thumbnail", "thumbnail.webp", "image/webp"));
    }

    private static ArtifactReferenceV1 CreateArtifact(
        string kind,
        string filename,
        string contentType) =>
        new(
            Guid.NewGuid(),
            kind,
            $"analyses/test/{filename}",
            contentType,
            100,
            new string('a', 64));
}
