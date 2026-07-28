using System.ComponentModel.DataAnnotations;

namespace YouTubeProcessing.Api;

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public int MaxInputLength { get; init; } = 2048;

    public BlobOptions Blob { get; init; } = new();
}

public sealed class BlobOptions
{
    [Required]
    public string Endpoint { get; init; } = "http://localhost:9000";

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string Bucket { get; init; } = "youtube-processing";

    public string Region { get; init; } = "us-east-1";
}
