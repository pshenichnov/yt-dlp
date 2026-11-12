using System.ComponentModel.DataAnnotations;

namespace YouTubeProcessing.Analyzer;

public sealed class AnalyzerOptions
{
    public const string SectionName = "Analyzer";

    [Required]
    public string Provider { get; init; } = "codex";
    [Required]
    public string CodexExecutable { get; init; } = "codex";
    [Required]
    public string Model { get; init; } = "gpt-5.6-sol";
    [Required]
    public string ReasoningEffort { get; init; } = "low";
    [Range(30, 1800)]
    public int ProviderTimeoutSeconds { get; init; } = 300;
    [Range(1024, 1_048_576)]
    public int MaxProviderOutputCharacters { get; init; } = 65_536;
    public string PromptVersion { get; init; } = "video-analysis-v2";
    public int MaxTranscriptCharacters { get; init; } = 2_000_000;
    public int ChunkCharacterLimit { get; init; } = 48_000;
    public BlobOptions Blob { get; init; } = new();
}

public sealed class BlobOptions
{
    [Required]
    public string Endpoint { get; init; } = "http://minio:9000";
    [Required]
    public string AccessKey { get; init; } = string.Empty;
    [Required]
    public string SecretKey { get; init; } = string.Empty;
    [Required]
    public string Bucket { get; init; } = "youtube-processing";
    public string Region { get; init; } = "us-east-1";
}
