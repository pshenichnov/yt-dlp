using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YouTubeProcessing.Api.Data;
using YouTubeProcessing.Api.Infrastructure;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Api.Endpoints;

public static partial class VideoCatalogEndpoints
{
    private const int DefaultLimit = 12;
    private const int MaximumLimit = 50;
    private const int DiscoveryPageSize = 12;

    public static IEndpointRouteBuilder MapVideoCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");
        group.MapPost("/channels", AddChannel);
        group.MapGet("/channels", GetChannels);
        group.MapGet("/channels/{channelId}", GetChannel);
        group.MapGet("/channels/{channelId}/videos", GetChannelVideos);
        group.MapPost("/channels/{channelId}/videos/pages", FetchNextChannelPage);
        group.MapGet("/videos", GetVideos);
        group.MapGet("/videos/{videoId}", GetVideo);
        return endpoints;
    }

    private static async Task<IResult> AddChannel(
        AddChannelRequest request,
        ApiDbContext db,
        YouTubeChannelResolver channelResolver,
        ISendEndpointProvider sender,
        CancellationToken cancellationToken)
    {
        var channelId = await channelResolver.ResolveChannelId(
            request.ChannelId,
            cancellationToken);
        if (channelId is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ChannelId)] =
                    ["Enter a valid YouTube channel URL, @handle, or channel ID."]
            });
        }

        var existing = await db.Channels.SingleOrDefaultAsync(
            x => x.Id == channelId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.NextPageStartIndex == 1 &&
                existing.SyncStatus != ChannelSyncStatuses.Queued)
            {
                existing.HasMoreVideos = true;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await QueuePage(existing, sender, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Accepted(
                    $"/api/v1/channels/{existing.Id}",
                    ToChannelResponse(
                        existing,
                        await CountVideos(db, channelId, cancellationToken)));
            }

            return Results.Ok(ToChannelResponse(
                existing,
                await CountVideos(db, channelId, cancellationToken)));
        }

        var now = DateTimeOffset.UtcNow;
        var channel = new ChannelEntity
        {
            Id = channelId,
            CreatedAt = now,
            UpdatedAt = now,
            NextPageStartIndex = 1,
            HasMoreVideos = true
        };
        db.Channels.Add(channel);
        await QueuePage(channel, sender, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Accepted(
            $"/api/v1/channels/{channel.Id}",
            ToChannelResponse(channel, 0));
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
                x.Handle,
                x.Videos.Count,
                x.UpdatedAt,
                x.SyncStatus,
                x.HasMoreVideos,
                x.LastSyncError,
                $"/api/v1/channels/{x.Id}",
                $"/api/v1/channels/{x.Id}/videos"))
            .ToListAsync(cancellationToken);

        return Results.Ok(channels);
    }

    private static async Task<IResult> GetChannel(
        string channelId,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var channel = await db.Channels.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == channelId,
            cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToChannelResponse(
            channel,
            await CountVideos(db, channelId, cancellationToken)));
    }

    private static async Task<IResult> FetchNextChannelPage(
        string channelId,
        ApiDbContext db,
        ISendEndpointProvider sender,
        CancellationToken cancellationToken)
    {
        var channel = await db.Channels.SingleOrDefaultAsync(
            x => x.Id == channelId,
            cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }
        if (channel.SyncStatus == ChannelSyncStatuses.Queued)
        {
            return Results.Conflict(new { message = "A channel page is already queued." });
        }
        if (!channel.HasMoreVideos)
        {
            return Results.Ok(ToChannelResponse(
                channel,
                await CountVideos(db, channelId, cancellationToken)));
        }

        await QueuePage(channel, sender, cancellationToken);
        channel.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted(
            $"/api/v1/channels/{channel.Id}",
            ToChannelResponse(channel, await CountVideos(db, channelId, cancellationToken)));
    }

    private static async Task QueuePage(
        ChannelEntity channel,
        ISendEndpointProvider sender,
        CancellationToken cancellationToken)
    {
        var requestId = NewId.NextGuid();
        channel.SyncStatus = ChannelSyncStatuses.Queued;
        channel.PendingRequestId = requestId;
        channel.LastSyncError = null;

        var endpoint = await sender.GetSendEndpoint(
            new Uri($"exchange:{QueueNames.ExtractorCommands}"));
        await endpoint.Send(
            new IntegrationEnvelopeV1<FetchChannelPagePayloadV1>(
                "1",
                NewId.NextGuid(),
                requestId,
                null,
                IntegrationMessageTypes.FetchChannelPage,
                DateTimeOffset.UtcNow,
                new FetchChannelPagePayloadV1(
                    requestId,
                    channel.Id,
                    channel.NextPageStartIndex,
                    DiscoveryPageSize)),
            context => context.CorrelationId = requestId,
            cancellationToken);
    }

    private static async Task<IResult> GetChannelVideos(
        string channelId,
        int? limit,
        int? offset,
        bool? processedOnly,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var channel = await db.Channels.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == channelId,
            cancellationToken);
        if (channel is null)
        {
            return Results.NotFound();
        }

        var normalizedLimit = NormalizeLimit(limit);
        var normalizedOffset = NormalizeOffset(offset);
        var filterProcessed = processedOnly == true;
        var videos = await LoadVideos(
            db,
            channelId,
            normalizedLimit + 1,
            normalizedOffset,
            filterProcessed,
            cancellationToken);
        var totalStoredVideoCount = await CountVideos(db, channelId, cancellationToken);
        var storedVideoCount = filterProcessed
            ? await CountVideos(db, channelId, cancellationToken, processedOnly: true)
            : totalStoredVideoCount;
        var hasStoredNextPage = videos.Count > normalizedLimit;
        var items = videos.Take(normalizedLimit).ToList();
        var hasRemoteNextPage = !filterProcessed && channel.HasMoreVideos;
        var hasMore = hasStoredNextPage || hasRemoteNextPage;
        var nextOffset = hasMore ? normalizedOffset + items.Count : (int?)null;

        return Results.Ok(new ChannelVideoPageResponse(
            items,
            normalizedOffset,
            normalizedLimit,
            storedVideoCount,
            totalStoredVideoCount,
            filterProcessed,
            hasStoredNextPage,
            hasRemoteNextPage,
            hasMore,
            nextOffset,
            channel.SyncStatus));
    }

    private static async Task<IResult> GetVideos(
        string? channelId,
        int? limit,
        int? offset,
        bool? processedOnly,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var videos = await LoadVideos(
            db,
            string.IsNullOrWhiteSpace(channelId) ? null : channelId,
            NormalizeLimit(limit),
            NormalizeOffset(offset),
            processedOnly == true,
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
        int limit,
        int offset,
        bool processedOnly,
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
        if (processedOnly)
        {
            query = query.Where(x => x.LatestAnalysisId != null);
        }

        var videos = await query
            .OrderByDescending(x => x.PublishedAt)
            .ThenByDescending(x => x.DiscoveredAt)
            .ThenBy(x => x.Id)
            .Skip(offset)
            .Take(limit)
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

        var analysisIds = videos
            .Where(x => x.LatestAnalysisId.HasValue)
            .Select(x => x.LatestAnalysisId!.Value)
            .ToArray();
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
            AnalysisEntity? analysis = null;
            Dictionary<string, ArtifactEntity>? videoArtifacts = null;
            if (video.LatestAnalysisId is Guid analysisId)
            {
                analyses.TryGetValue(analysisId, out analysis);
                artifactsByAnalysis.TryGetValue(analysisId, out videoArtifacts);
            }

            ArtifactEntity? transcript = null;
            ArtifactEntity? thumbnail = null;
            videoArtifacts?.TryGetValue("transcript", out transcript);
            videoArtifacts?.TryGetValue("thumbnail", out thumbnail);
            var subjects = analysis?.SubjectsJson is null
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
                video.Description,
                video.ThumbnailUrl,
                video.LatestAnalysisId,
                analysis?.Summary,
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

    private static ChannelCatalogResponse ToChannelResponse(ChannelEntity channel, int videoCount) =>
        new(
            channel.Id,
            channel.Name,
            channel.Handle,
            videoCount,
            channel.UpdatedAt,
            channel.SyncStatus,
            channel.HasMoreVideos,
            channel.LastSyncError,
            $"/api/v1/channels/{channel.Id}",
            $"/api/v1/channels/{channel.Id}/videos");

    private static Task<int> CountVideos(
        ApiDbContext db,
        string channelId,
        CancellationToken cancellationToken,
        bool processedOnly = false) =>
        db.Videos.CountAsync(
            x => x.ChannelId == channelId && (!processedOnly || x.LatestAnalysisId != null),
            cancellationToken);

    private static int NormalizeLimit(int? limit) =>
        Math.Clamp(limit ?? DefaultLimit, 1, MaximumLimit);

    private static int NormalizeOffset(int? offset) => Math.Max(offset ?? 0, 0);

}

public sealed record AddChannelRequest(string ChannelId);

public sealed record ChannelCatalogResponse(
    string ChannelId,
    string? Name,
    string? Handle,
    int VideoCount,
    DateTimeOffset UpdatedAt,
    string SyncStatus,
    bool HasMoreVideos,
    string? LastSyncError,
    string StatusUrl,
    string VideosUrl);

public sealed record ChannelVideoPageResponse(
    IReadOnlyList<StoredVideoResponse> Items,
    int Offset,
    int Limit,
    int StoredVideoCount,
    int TotalStoredVideoCount,
    bool ProcessedOnly,
    bool HasStoredNextPage,
    bool HasRemoteNextPage,
    bool HasMore,
    int? NextOffset,
    string SyncStatus);

public sealed record StoredVideoResponse(
    VideoResponse Video,
    string? Description,
    string? ThumbnailUrl,
    Guid? LatestAnalysisId,
    string? Summary,
    IReadOnlyList<string> Subjects,
    ArtifactResponse? Transcript,
    ArtifactResponse? Thumbnail,
    DateTimeOffset? FirstProcessedAt,
    DateTimeOffset? LastProcessedAt);
