using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace YouTubeProcessing.Api.Health;

public interface IRabbitMqManagementProbe
{
    Task<RabbitMqSnapshot> InspectAsync(CancellationToken cancellationToken);
}

public sealed class RabbitMqManagementProbe(
    HttpClient client,
    IConfiguration configuration) : IRabbitMqManagementProbe
{
    public async Task<RabbitMqSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        var virtualHost = configuration["RabbitMq:VirtualHost"] ?? "/";
        var requestPath = $"api/queues/{Uri.EscapeDataString(virtualHost)}";
        using var response = await client.GetAsync(requestPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        var queues = await response.Content.ReadFromJsonAsync<RabbitMqQueueResponse[]>(
            cancellationToken: cancellationToken) ?? [];
        return new RabbitMqSnapshot(queues.ToDictionary(
            queue => queue.Name,
            queue => new RabbitMqQueueSnapshot(queue.Name, queue.Consumers, queue.State),
            StringComparer.Ordinal));
    }

    private sealed record RabbitMqQueueResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("consumers")] int Consumers,
        [property: JsonPropertyName("state")] string? State);
}
