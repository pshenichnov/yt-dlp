using Microsoft.OpenApi;
using YouTubeProcessing.Api.Infrastructure;

namespace YouTubeProcessing.Api.Tests;

public sealed class ApiKeyOpenApiDocumentTransformerTests
{
    [Fact]
    public async Task AddsApiKeySecuritySchemeAndRequirement()
    {
        var document = new OpenApiDocument();
        var transformer = new ApiKeyOpenApiDocumentTransformer();

        await transformer.TransformAsync(document, null!, CancellationToken.None);

        var scheme = Assert.IsType<OpenApiSecurityScheme>(
            document.Components!.SecuritySchemes![ApiKeyOpenApiDocumentTransformer.SchemeName]);
        Assert.Equal(SecuritySchemeType.ApiKey, scheme.Type);
        Assert.Equal(ApiKeyMiddleware.HeaderName, scheme.Name);
        Assert.Equal(ParameterLocation.Header, scheme.In);
        Assert.Single(document.Security!);
    }
}
