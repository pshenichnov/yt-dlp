using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace YouTubeProcessing.Api.Infrastructure;

public sealed class ApiKeyOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "ApiKey";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyMiddleware.HeaderName,
            In = ParameterLocation.Header,
            Description = "API key required for all non-health endpoints."
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = []
        });

        return Task.CompletedTask;
    }
}
