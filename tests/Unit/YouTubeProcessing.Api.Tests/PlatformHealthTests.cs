using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using YouTubeProcessing.Api.Health;

namespace YouTubeProcessing.Api.Tests;

public sealed class PlatformHealthTests
{
    [Fact]
    public async Task RabbitMqProbeReturnsQueueConsumerState()
    {
        var handler = new StubHttpMessageHandler(
            """
            [
              {"name":"worker.commands","consumers":2,"state":"running"}
            ]
            """);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://rabbitmq:15672/")
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:VirtualHost"] = "/"
            })
            .Build();
        var probe = new RabbitMqManagementProbe(client, configuration);

        var snapshot = await probe.InspectAsync(CancellationToken.None);

        var queue = Assert.Single(snapshot.Queues).Value;
        Assert.Equal("worker.commands", queue.Name);
        Assert.Equal(2, queue.Consumers);
        Assert.Equal("running", queue.State);
        Assert.Equal("/api/queues/%2F", handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public void MicroserviceIsHealthyWhenEveryRequiredQueueHasAConsumer()
    {
        var snapshot = new RabbitMqSnapshot(
            new Dictionary<string, RabbitMqQueueSnapshot>
            {
                ["commands"] = new("commands", 1, "running"),
                ["events"] = new("events", 1, "running")
            });

        var health = PlatformHealthService.MicroserviceHealthEvaluator.Evaluate(
            snapshot,
            12,
            "commands",
            "events");

        Assert.Equal(HealthStatuses.Healthy, health.Status);
        Assert.Equal(12, health.DurationMilliseconds);
        Assert.All(health.Queues!.Values, queue =>
            Assert.Equal(HealthStatuses.Healthy, queue.Status));
    }

    [Theory]
    [InlineData(0, "running")]
    [InlineData(1, "down")]
    public void MicroserviceIsUnhealthyWithoutARunningConsumer(
        int consumers,
        string state)
    {
        var snapshot = new RabbitMqSnapshot(
            new Dictionary<string, RabbitMqQueueSnapshot>
            {
                ["commands"] = new("commands", consumers, state)
            });

        var health = PlatformHealthService.MicroserviceHealthEvaluator.Evaluate(
            snapshot,
            4,
            "commands");

        Assert.Equal(HealthStatuses.Unhealthy, health.Status);
        Assert.Equal(HealthStatuses.Unhealthy, health.Queues!["commands"].Status);
    }

    [Fact]
    public void MicroserviceIsUnhealthyWhenRequiredQueueIsMissing()
    {
        var health = PlatformHealthService.MicroserviceHealthEvaluator.Evaluate(
            new RabbitMqSnapshot(
                new Dictionary<string, RabbitMqQueueSnapshot>()),
            1,
            "commands");

        Assert.Equal(HealthStatuses.Unhealthy, health.Status);
        Assert.Equal(0, health.Queues!["commands"].Consumers);
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
