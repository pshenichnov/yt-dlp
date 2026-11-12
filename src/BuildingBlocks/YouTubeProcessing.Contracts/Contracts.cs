using System.Text.Json.Serialization;

namespace YouTubeProcessing.Contracts;

[JsonConverter(typeof(AnalysisScenarioJsonConverter))]
public enum AnalysisScenarioV1
{
    [JsonStringEnumMemberName("summary")]
    Summary,

    [JsonStringEnumMemberName("topics")]
    Topics
}

public sealed class AnalysisScenarioJsonConverter()
    : JsonStringEnumConverter<AnalysisScenarioV1>(
        namingPolicy: null,
        allowIntegerValues: false);

public static class AnalysisScenarioDefaults
{
    public static AnalysisScenarioV1[] All =>
        [AnalysisScenarioV1.Summary, AnalysisScenarioV1.Topics];

    public static AnalysisScenarioV1[] Normalize(
        IEnumerable<AnalysisScenarioV1>? scenarios) =>
        scenarios?.Distinct().ToArray() is { Length: > 0 } selected
            ? selected
            : All;
}

public static class QueueNames
{
    public const string Orchestrator = "youtube.orchestrator.v1";
    public const string Analyzer = "youtube.analyzer.v1";
    public const string ApiProjection = "youtube.api-projection.v1";
    public const string ExtractorCommands = "youtube.extract-video.commands.v1";
    public const string ExtractorEvents = "youtube.extract-video.events.v1";
}

public static class JobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class JobStages
{
    public const string Queued = "queued";
    public const string Extracting = "extractingTranscript";
    public const string Analyzing = "analyzingTranscript";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ArtifactReferenceV1(
    Guid ArtifactId,
    string Kind,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed record VideoMetadataV1(
    string VideoId,
    string Title,
    string? ChannelId,
    string? ChannelName,
    DateTimeOffset? PublishedAt,
    long? DurationSeconds,
    long? ViewCount,
    long? LikeCount,
    string? Description,
    string WebpageUrl);

public sealed record VideoAnalysisRequestedV1(
    Guid JobId,
    Guid AnalysisId,
    string VideoId,
    string OriginalInput,
    string? PreferredLanguage,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<AnalysisScenarioV1>? Scenarios = null);

public sealed record JobStageChangedV1(
    Guid JobId,
    Guid AnalysisId,
    string Status,
    string Stage,
    int ProgressPercent,
    DateTimeOffset OccurredAt);

public sealed record YouTubeVideoExtractionCompletedV1(
    Guid JobId,
    Guid AnalysisId,
    VideoMetadataV1 Video,
    ArtifactReferenceV1 Transcript,
    ArtifactReferenceV1? OriginalCaption,
    ArtifactReferenceV1? TranscriptSegments,
    ArtifactReferenceV1 Thumbnail,
    DateTimeOffset CompletedAt);

public sealed record YouTubeVideoExtractionFailedV1(
    Guid JobId,
    Guid AnalysisId,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    DateTimeOffset FailedAt);

public sealed record AnalyzeTranscriptV1(
    Guid JobId,
    Guid AnalysisId,
    VideoMetadataV1 Video,
    ArtifactReferenceV1 Transcript,
    ArtifactReferenceV1 Thumbnail,
    string PromptVersion,
    DateTimeOffset RequestedAt,
    IReadOnlyList<AnalysisScenarioV1>? Scenarios = null);

public sealed record TranscriptAnalyzedV1(
    Guid JobId,
    Guid AnalysisId,
    string Summary,
    IReadOnlyList<string> Subjects,
    string Model,
    string PromptVersion,
    DateTimeOffset CompletedAt);

public sealed record TranscriptAnalysisFailedV1(
    Guid JobId,
    Guid AnalysisId,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    DateTimeOffset FailedAt);

public sealed record VideoAnalysisCompletedV1(
    Guid JobId,
    Guid AnalysisId,
    VideoMetadataV1 Video,
    ArtifactReferenceV1 Transcript,
    ArtifactReferenceV1? OriginalCaption,
    ArtifactReferenceV1? TranscriptSegments,
    ArtifactReferenceV1 Thumbnail,
    string Summary,
    IReadOnlyList<string> Subjects,
    string Model,
    string PromptVersion,
    DateTimeOffset CompletedAt);

public sealed record VideoAnalysisFailedV1(
    Guid JobId,
    Guid AnalysisId,
    string Stage,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset FailedAt);

public sealed record ExtractVideoPayloadV1(
    Guid JobId,
    Guid AnalysisId,
    string VideoId,
    string OriginalInput,
    string? PreferredLanguage);

public sealed record ExtractVideoCompletedPayloadV1(
    Guid JobId,
    Guid AnalysisId,
    VideoMetadataV1 Video,
    ArtifactReferenceV1 Transcript,
    ArtifactReferenceV1? OriginalCaption,
    ArtifactReferenceV1? TranscriptSegments,
    ArtifactReferenceV1 Thumbnail);

public sealed record ExtractVideoFailedPayloadV1(
    Guid JobId,
    Guid AnalysisId,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient);

public sealed record IntegrationEnvelopeV1<TPayload>(
    string SchemaVersion,
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    string MessageType,
    DateTimeOffset OccurredAt,
    TPayload Payload);

public sealed record ExtractorEventEnvelopeV1(
    string SchemaVersion,
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    string MessageType,
    DateTimeOffset OccurredAt,
    System.Text.Json.JsonElement Payload);

public static class IntegrationMessageTypes
{
    public const string ExtractVideo = "extract-video";
    public const string ExtractVideoCompleted = "extract-video-completed";
    public const string ExtractVideoFailed = "extract-video-failed";
}
