using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace YouTubeProcessing.Api.Tests;

public sealed class ApiRoutingTests(ApiRoutingTests.ApiFactory factory)
    : IClassFixture<ApiRoutingTests.ApiFactory>
{
    [Fact]
    public async Task HomePageLinksToStatusPage()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("href=\"/status\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/status")]
    [InlineData("/health.html")]
    public async Task HealthPageRoutesAreAnonymous(string path)
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("System status", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health.js", "text/javascript")]
    [InlineData("/health.css", "text/css")]
    public async Task HealthAssetsAreAnonymousAndHaveExpectedContentTypes(
        string path,
        string contentType)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MissingPublicAssetReturnsNotFoundInsteadOfAuthenticationError()
    {
        using var missingAssetFactory = new MissingAssetFactory();
        using var client = missingAssetFactory.CreateClient();

        var response = await client.GetAsync("/health.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnhealthyHealthResponseRetainsJsonContractAnd503Status()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"status\":\"unhealthy\"", body);
        Assert.Contains("\"services\"", body);
    }

    [Fact]
    public async Task BusinessApiStillRequiresApiKey()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/jobs/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrations"] = "false",
                    ["ConnectionStrings:ApiDatabase"] =
                        "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1;Command Timeout=1",
                    ["Health:RabbitMqManagementEndpoint"] = "http://127.0.0.1:1",
                    ["Health:TimeoutSeconds"] = "1",
                    ["Platform:Blob:Endpoint"] = "http://127.0.0.1:1"
                }));
            builder.ConfigureTestServices(services =>
                services.RemoveAll<IHostedService>());
        }
    }

    private sealed class MissingAssetFactory : ApiFactory
    {
        private readonly string webRoot = Path.Combine(
            Path.GetTempPath(),
            $"youtube-processing-api-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(webRoot);
            base.ConfigureWebHost(builder);
            builder.UseWebRoot(webRoot);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(webRoot))
            {
                Directory.Delete(webRoot, recursive: true);
            }
        }
    }
}
