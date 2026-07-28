using System.ComponentModel.DataAnnotations;

namespace YouTubeProcessing.Analyzer;

public sealed class AnalyzerOptions
{
    public const string SectionName = "Analyzer";

    public string Provider { get; init; } = "ollama";
    public string OllamaEndpoint { get; init; } = "http://ollama:11434";
    public string Model { get; init; } = "llama3.2:3b";
    public string PromptVersion { get; init; } = "video-summary-v1";
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
