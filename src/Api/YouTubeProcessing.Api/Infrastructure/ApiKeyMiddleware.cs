using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace YouTubeProcessing.Api.Infrastructure;

public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Api-Key";

    private static readonly HashSet<PathString> PublicUiPaths =
    [
        new("/"),
        new("/index.html"),
        new("/app.js"),
        new("/styles.css"),
        new("/status"),
        new("/health.html"),
        new("/health.js"),
        new("/health.css")
    ];

    public async Task InvokeAsync(HttpContext context, IOptions<PlatformOptions> options)
    {
        if (PublicUiPaths.Contains(context.Request.Path) ||
            context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal) ||
            context.Request.Path.StartsWithSegments("/openapi") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await next(context);
            return;
        }

        var supplied = context.Request.Headers[HeaderName].ToString();
        var expected = options.Value.ApiKey;
        var valid = supplied.Length == expected.Length &&
                    CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(supplied),
                        Encoding.UTF8.GetBytes(expected));

        if (!valid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.com/401",
                title = $"A valid {HeaderName} header is required.",
                status = 401
            });
            return;
        }

        await next(context);
    }
}
