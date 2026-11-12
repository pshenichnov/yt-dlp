using System.ComponentModel.DataAnnotations;

namespace YouTubeProcessing.Api.Health;

public static class HealthStatuses
{
    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";
    public const string Disabled = "disabled";
}

public sealed class PlatformHealthOptions
{
    public const string SectionName = "Health";

    [Required]
    [Url]
    public string RabbitMqManagementEndpoint { get; init; } = "http://rabbitmq:15672";

    [Range(1, 30)]
    public int TimeoutSeconds { get; init; } = 3;

    public bool AnalyzerEnabled { get; init; }
}

public sealed record QueueHealth(
    string Status,
    int Consumers);

public sealed record ComponentHealth(
    string Status,
    long DurationMilliseconds,
    string Description,
    IReadOnlyDictionary<string, QueueHealth>? Queues = null);

public sealed record PlatformHealthResponse(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyDictionary<string, ComponentHealth> Services);

public sealed record RabbitMqQueueSnapshot(
    string Name,
    int Consumers,
    string? State);

public sealed record RabbitMqSnapshot(
    IReadOnlyDictionary<string, RabbitMqQueueSnapshot> Queues);
