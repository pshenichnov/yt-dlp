using System.Text.RegularExpressions;

namespace YouTubeProcessing.Api.Infrastructure;

public static partial class VideoInputCanonicalizer
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();

    public static bool TryCanonicalize(string? input, int maxLength, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > maxLength)
        {
            return false;
        }

        input = input.Trim();
        if (VideoIdPattern().IsMatch(input))
        {
            videoId = input;
            return true;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        string? candidate = null;

        if (host is "youtu.be" or "www.youtu.be")
        {
            candidate = uri.AbsolutePath.Trim('/').Split('/')[0];
        }
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                candidate = ParseQuery(uri.Query).GetValueOrDefault("v");
            }
            else
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length >= 2 && parts[0] is "shorts" or "embed" or "live")
                {
                    candidate = parts[1];
                }
            }
        }

        if (candidate is not null && VideoIdPattern().IsMatch(candidate))
        {
            videoId = candidate;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.OrdinalIgnoreCase);
}
