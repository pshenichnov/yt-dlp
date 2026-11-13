using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YouTubeProcessing.Api.Data;

namespace YouTubeProcessing.Api.Endpoints;

public static class VideoCatalogEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 200;

    public static IEndpointRouteBuilder MapVideoCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");
        group.MapGet("/channels", GetChannels);
        group.MapGet("/channels/{channelId}/videos", GetChannelVideos);
        group.MapGet("/videos", GetVideos);
        group.MapGet("/videos/{videoId}", GetVideo);
        return endpoints;
    }

    private static async Task<IResult> GetChannels(
        int? limit,
        int? offset,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var channels = await db.Channels
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Name)
            .Skip(NormalizeOffset(offset))
            .Take(NormalizeLimit(limit))
            .Select(x => new ChannelCatalogResponse(
                x.Id,
                x.Name,
                x.Videos.Count,
                x.UpdatedAt,
                $"/api/v1/channels/{x.Id}/videos"))
            .ToListAsync(cancellationToken);

        return Results.Ok(channels);
    }

    private static async Task<IResult> GetChannelVideos(
        string channelId,
        int? limit,
        int? offset,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Channels.AnyAsync(x => x.Id == channelId, cancellationToken))
        {
            return Results.NotFound();
        }

        var videos = await LoadVideos(
            db,
            channelId,
            limit,
            offset,
            cancellationToken);
        return Results.Ok(videos);
    }

    private static async Task<IResult> GetVideos(
        string? channelId,
        int? limit,
        int? offset,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var videos = await LoadVideos(
            db,
            string.IsNullOrWhiteSpace(channelId) ? null : channelId,
            limit,
            offset,
            cancellationToken);
        return Results.Ok(videos);
    }

    private static async Task<IResult> GetVideo(
        string videoId,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var video = await db.Videos
            .AsNoTracking()
            .Include(x => x.Channel)
            .SingleOrDefaultAsync(x => x.Id == videoId, cancellationToken);
        if (video is null)
        {
            return Results.NotFound();
        }

        var responses = await BuildResponses(db, [video], cancellationToken);
        return Results.Ok(responses[0]);
    }

    private static async Task<IReadOnlyList<StoredVideoResponse>> LoadVideos(
        ApiDbContext db,
        string? channelId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var query = db.Videos
            .AsNoTracking()
            .Include(x => x.Channel)
            .AsQueryable();
        if (channelId is not null)
        {
            query = query.Where(x => x.ChannelId == channelId);
        }

        var videos = await query
            .OrderByDescending(x => x.LastProcessedAt)
            .ThenByDescending(x => x.PublishedAt)
            .Skip(NormalizeOffset(offset))
            .Take(NormalizeLimit(limit))
            .ToListAsync(cancellationToken);

        return await BuildResponses(db, videos, cancellationToken);
    }

    private static async Task<IReadOnlyList<StoredVideoResponse>> BuildResponses(
        ApiDbContext db,
        IReadOnlyList<VideoEntity> videos,
        CancellationToken cancellationToken)
    {
        if (videos.Count == 0)
        {
            return [];
        }

        var analysisIds = videos.Select(x => x.LatestAnalysisId).ToArray();
        var analyses = await db.Analyses
            .AsNoTracking()
            .Where(x => analysisIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var artifacts = await db.Artifacts
            .AsNoTracking()
            .Where(x => analysisIds.Contains(x.AnalysisId))
            .ToListAsync(cancellationToken);
        var artifactsByAnalysis = artifacts
            .GroupBy(x => x.AnalysisId)
            .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Kind));

        return videos.Select(video =>
        {
            var analysis = analyses[video.LatestAnalysisId];
            artifactsByAnalysis.TryGetValue(video.LatestAnalysisId, out var videoArtifacts);
            ArtifactEntity? transcript = null;
            ArtifactEntity? thumbnail = null;
            if (videoArtifacts is not null)
            {
                videoArtifacts.TryGetValue("transcript", out transcript);
                videoArtifacts.TryGetValue("thumbnail", out thumbnail);
            }
            var subjects = analysis.SubjectsJson is null
                ? []
                : JsonSerializer.Deserialize<string[]>(analysis.SubjectsJson) ?? [];

            return new StoredVideoResponse(
                new VideoResponse(
                    video.Id,
                    video.WebpageUrl,
                    video.Title,
                    video.ChannelId,
                    video.Channel?.Name,
                    video.PublishedAt,
                    video.DurationSeconds,
                    video.ViewCount,
                    video.LikeCount),
                video.LatestAnalysisId,
                analysis.Summary ?? string.Empty,
                subjects,
                transcript is null ? null : ToArtifactResponse(transcript),
                thumbnail is null ? null : ToArtifactResponse(thumbnail),
                video.FirstProcessedAt,
                video.LastProcessedAt);
        }).ToList();
    }

    private static ArtifactResponse ToArtifactResponse(ArtifactEntity artifact) =>
        new(
            artifact.Id,
            artifact.Kind,
            artifact.ContentType,
            artifact.SizeBytes,
            artifact.Sha256,
            $"/api/v1/artifacts/{artifact.Id}");

    private static int NormalizeLimit(int? limit) =>
        Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);

    private static int NormalizeOffset(int? offset) => Math.Max(offset ?? 0, 0);
}

public sealed record ChannelCatalogResponse(
    string ChannelId,
    string? Name,
    int VideoCount,
    DateTimeOffset UpdatedAt,
    string VideosUrl);

public sealed record StoredVideoResponse(
    VideoResponse Video,
    Guid LatestAnalysisId,
    string Summary,
    IReadOnlyList<string> Subjects,
    ArtifactResponse? Transcript,
    ArtifactResponse? Thumbnail,
    DateTimeOffset FirstProcessedAt,
    DateTimeOffset LastProcessedAt);
