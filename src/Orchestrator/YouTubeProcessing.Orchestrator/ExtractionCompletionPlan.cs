using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Orchestrator;

public sealed record ExtractionCompletionPlan(
    string NextStage,
    JobStageChangedV1? StageChange,
    AnalyzeTranscriptV1? AnalysisRequest,
    VideoAnalysisCompletedV1? Completion)
{
    public static ExtractionCompletionPlan Create(
        ExtractVideoCompletedPayloadV1 extraction,
        bool analysisEnabled,
        DateTimeOffset occurredAt)
    {
        if (analysisEnabled)
        {
            return new ExtractionCompletionPlan(
                JobStages.Analyzing,
                new JobStageChangedV1(
                    extraction.JobId,
                    extraction.AnalysisId,
                    JobStatuses.Running,
                    JobStages.Analyzing,
                    65,
                    occurredAt),
                new AnalyzeTranscriptV1(
                    extraction.JobId,
                    extraction.AnalysisId,
                    extraction.Video,
                    extraction.Transcript,
                    extraction.Thumbnail,
                    "video-summary-v1",
                    occurredAt),
                null);
        }

        return new ExtractionCompletionPlan(
            JobStages.Completed,
            null,
            null,
            new VideoAnalysisCompletedV1(
                extraction.JobId,
                extraction.AnalysisId,
                extraction.Video,
                extraction.Transcript,
                extraction.OriginalCaption,
                extraction.TranscriptSegments,
                extraction.Thumbnail,
                string.Empty,
                [],
                string.Empty,
                string.Empty,
                occurredAt));
    }
}
