using System.Text.RegularExpressions;

namespace YouTubeProcessing.Api.Infrastructure;

public sealed partial class YouTubeChannelResolver(HttpClient client)
{
    private const int MaximumInputLength = 2048;
    private const int MaximumResponseCharacters = 4 * 1024 * 1024;
    private const int SearchOverlapCharacters = 128;

    public async Task<string?> ResolveChannelId(
        string? input,
        CancellationToken cancellationToken)
    {
        if (!TryParseInput(input, out var channelId, out var handle))
        {
            return null;
        }

        if (channelId is not null)
        {
            return channelId;
        }

        try
        {
            using var response = await client.GetAsync(
                $"https://www.youtube.com/@{Uri.EscapeDataString(handle!)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(
                stream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 8192,
                leaveOpen: false);
            var buffer = new char[8192];
            var charactersRead = 0;
            var overlap = string.Empty;
            while (charactersRead < MaximumResponseCharacters)
            {
                var remaining = Math.Min(
                    buffer.Length,
                    MaximumResponseCharacters - charactersRead);
                var read = await reader.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                charactersRead += read;
                var content = overlap + new string(buffer, 0, read);
                var match = ExternalChannelId().Match(content);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                overlap = content[^Math.Min(SearchOverlapCharacters, content.Length)..];
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    internal static bool TryParseInput(
        string? input,
        out string? channelId,
        out string? handle)
    {
        channelId = null;
        handle = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumInputLength)
        {
            return false;
        }

        input = input.Trim();
        if (YouTubeChannelId().IsMatch(input))
        {
            channelId = input;
            return true;
        }

        if (input.StartsWith('@') && YouTubeHandle().IsMatch(input[1..]))
        {
            handle = input[1..];
            return true;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.ToLowerInvariant() is not ("youtube.com" or "www.youtube.com" or "m.youtube.com"))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            parts[0].Equals("channel", StringComparison.OrdinalIgnoreCase) &&
            YouTubeChannelId().IsMatch(parts[1]))
        {
            channelId = parts[1];
            return true;
        }

        if (parts.Length >= 1 && parts[0].StartsWith('@'))
        {
            var candidate = Uri.UnescapeDataString(parts[0][1..]);
            if (YouTubeHandle().IsMatch(candidate))
            {
                handle = candidate;
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^UC[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeChannelId();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeHandle();

    [GeneratedRegex("\"externalId\"\\s*:\\s*\"(UC[A-Za-z0-9_-]{22})\"", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalChannelId();
}
