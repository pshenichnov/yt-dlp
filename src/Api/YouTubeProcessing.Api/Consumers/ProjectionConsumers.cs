using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YouTubeProcessing.Api.Data;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Api.Consumers;

public sealed class JobStageChangedConsumer(ApiDbContext db) : IConsumer<JobStageChangedV1>
{
    public async Task Consume(ConsumeContext<JobStageChangedV1> context)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(
            x => x.Id == context.Message.JobId,
            context.CancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = context.Message.Status;
        job.Stage = context.Message.Stage;
        job.ProgressPercent = context.Message.ProgressPercent;
        job.UpdatedAt = context.Message.OccurredAt;
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class VideoAnalysisCompletedConsumer(ApiDbContext db)
    : IConsumer<VideoAnalysisCompletedV1>
{
    public async Task Consume(ConsumeContext<VideoAnalysisCompletedV1> context)
    {
        var message = context.Message;
        var job = await db.Jobs.SingleAsync(x => x.Id == message.JobId, context.CancellationToken);
        var analysis = await db.Analyses.SingleAsync(
            x => x.Id == message.AnalysisId,
            context.CancellationToken);

        job.Status = JobStatuses.Completed;
        job.Stage = JobStages.Completed;
        job.ProgressPercent = 100;
        job.UpdatedAt = message.CompletedAt;
        job.CompletedAt = message.CompletedAt;

        analysis.Title = message.Video.Title;
        analysis.ChannelId = message.Video.ChannelId;
        analysis.ChannelName = message.Video.ChannelName;
        analysis.PublishedAt = message.Video.PublishedAt;
        analysis.DurationSeconds = message.Video.DurationSeconds;
        analysis.ViewCount = message.Video.ViewCount;
        analysis.LikeCount = message.Video.LikeCount;
        analysis.VideoDescription = message.Video.Description;
        analysis.WebpageUrl = message.Video.WebpageUrl;
        analysis.Summary = message.Summary;
        analysis.SubjectsJson = JsonSerializer.Serialize(message.Subjects);
        analysis.AnalysisModel = string.IsNullOrWhiteSpace(message.Model) ? null : message.Model;
        analysis.PromptVersion = string.IsNullOrWhiteSpace(message.PromptVersion)
            ? null
            : message.PromptVersion;
        analysis.CompletedAt = message.CompletedAt;

        AddArtifactIfMissing(message.AnalysisId, message.Transcript, message.CompletedAt);
        if (message.OriginalCaption is not null)
        {
            AddArtifactIfMissing(message.AnalysisId, message.OriginalCaption, message.CompletedAt);
        }
        if (message.TranscriptSegments is not null)
        {
            AddArtifactIfMissing(message.AnalysisId, message.TranscriptSegments, message.CompletedAt);
        }
        AddArtifactIfMissing(message.AnalysisId, message.Thumbnail, message.CompletedAt);

        await UpsertCatalog(message, context.CancellationToken);

        await db.SaveChangesAsync(context.CancellationToken);
    }

    private async Task UpsertCatalog(
        VideoAnalysisCompletedV1 message,
        CancellationToken cancellationToken)
    {
        var channelId = string.IsNullOrWhiteSpace(message.Video.ChannelId)
            ? null
            : message.Video.ChannelId;

        if (channelId is not null)
        {
            var channel = await db.Channels.SingleOrDefaultAsync(
                x => x.Id == channelId,
                cancellationToken);
            if (channel is null)
            {
                db.Channels.Add(new ChannelEntity
                {
                    Id = channelId,
                    Name = message.Video.ChannelName,
                    CreatedAt = message.CompletedAt,
                    UpdatedAt = message.CompletedAt,
                    HasMoreVideos = false
                });
            }
            else
            {
                if (message.CompletedAt >= channel.UpdatedAt &&
                    !string.IsNullOrWhiteSpace(message.Video.ChannelName))
                {
                    channel.Name = message.Video.ChannelName;
                }
                if (message.CompletedAt > channel.UpdatedAt)
                {
                    channel.UpdatedAt = message.CompletedAt;
                }
            }
        }

        var video = await db.Videos.SingleOrDefaultAsync(
            x => x.Id == message.Video.VideoId,
            cancellationToken);
        if (video is null)
        {
            db.Videos.Add(new VideoEntity
            {
                Id = message.Video.VideoId,
                ChannelId = channelId,
                LatestAnalysisId = message.AnalysisId,
                Title = message.Video.Title,
                Description = message.Video.Description,
                WebpageUrl = message.Video.WebpageUrl,
                PublishedAt = message.Video.PublishedAt,
                DurationSeconds = message.Video.DurationSeconds,
                ViewCount = message.Video.ViewCount,
                LikeCount = message.Video.LikeCount,
                FirstProcessedAt = message.CompletedAt,
                LastProcessedAt = message.CompletedAt,
                DiscoveredAt = message.CompletedAt
            });
            return;
        }

        if (video.FirstProcessedAt is null || message.CompletedAt < video.FirstProcessedAt)
        {
            video.FirstProcessedAt = message.CompletedAt;
        }
        if (message.CompletedAt < video.LastProcessedAt)
        {
            return;
        }

        video.ChannelId = channelId;
        video.LatestAnalysisId = message.AnalysisId;
        video.Title = message.Video.Title;
        video.Description = message.Video.Description;
        video.WebpageUrl = message.Video.WebpageUrl;
        video.PublishedAt = message.Video.PublishedAt;
        video.DurationSeconds = message.Video.DurationSeconds;
        video.ViewCount = message.Video.ViewCount;
        video.LikeCount = message.Video.LikeCount;
        video.LastProcessedAt = message.CompletedAt;
    }

    private void AddArtifactIfMissing(
        Guid analysisId,
        ArtifactReferenceV1 artifact,
        DateTimeOffset createdAt)
    {
        if (db.Artifacts.Local.Any(x => x.Id == artifact.ArtifactId))
        {
            return;
        }

        db.Artifacts.Add(new ArtifactEntity
        {
            Id = artifact.ArtifactId,
            AnalysisId = analysisId,
            Kind = artifact.Kind,
            ObjectKey = artifact.ObjectKey,
            ContentType = artifact.ContentType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256,
            CreatedAt = createdAt
        });
    }
}

public sealed class VideoAnalysisFailedConsumer(ApiDbContext db)
    : IConsumer<VideoAnalysisFailedV1>
{
    public async Task Consume(ConsumeContext<VideoAnalysisFailedV1> context)
    {
        var message = context.Message;
        var job = await db.Jobs.SingleOrDefaultAsync(
            x => x.Id == message.JobId,
            context.CancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = JobStatuses.Failed;
        job.Stage = JobStages.Failed;
        job.ErrorCode = message.ErrorCode;
        job.ErrorMessage = message.ErrorMessage;
        job.UpdatedAt = message.FailedAt;
        job.CompletedAt = message.FailedAt;
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class ChannelCatalogEventConsumer(
    ApiDbContext db,
    ILogger<ChannelCatalogEventConsumer> logger) : IConsumer<ExtractorEventEnvelopeV1>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task Consume(ConsumeContext<ExtractorEventEnvelopeV1> context)
    {
        var envelope = context.Message;
        switch (envelope.MessageType)
        {
            case IntegrationMessageTypes.FetchChannelPageCompleted:
                await CompletePage(envelope, context.CancellationToken);
                break;
            case IntegrationMessageTypes.FetchChannelPageFailed:
                await FailPage(envelope, context.CancellationToken);
                break;
        }
    }

    private async Task CompletePage(
        ExtractorEventEnvelopeV1 envelope,
        CancellationToken cancellationToken)
    {
        var result = envelope.Payload.Deserialize<FetchChannelPageCompletedPayloadV1>(JsonOptions)
            ?? throw new InvalidOperationException("Channel page completion payload is invalid.");
        var channel = await db.Channels.SingleOrDefaultAsync(
            x => x.Id == result.ChannelId,
            cancellationToken);
        if (channel is null || channel.PendingRequestId != result.RequestId)
        {
            logger.LogWarning(
                "Ignoring stale channel page {RequestId} for {ChannelId}",
                result.RequestId,
                result.ChannelId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.ChannelName))
        {
            channel.Name = result.ChannelName;
        }
        if (!string.IsNullOrWhiteSpace(result.ChannelHandle))
        {
            channel.Handle = result.ChannelHandle;
        }

        foreach (var item in result.Videos)
        {
            var video = await db.Videos.SingleOrDefaultAsync(
                x => x.Id == item.VideoId,
                cancellationToken);
            if (video is null)
            {
                db.Videos.Add(new VideoEntity
                {
                    Id = item.VideoId,
                    ChannelId = channel.Id,
                    Title = item.Title,
                    Description = item.Description,
                    WebpageUrl = item.WebpageUrl,
                    ThumbnailUrl = item.ThumbnailUrl,
                    PublishedAt = item.PublishedAt,
                    DurationSeconds = item.DurationSeconds,
                    DiscoveredAt = envelope.OccurredAt
                });
                continue;
            }

            video.ChannelId = channel.Id;
            video.Title = item.Title;
            video.Description = item.Description;
            video.WebpageUrl = item.WebpageUrl;
            video.ThumbnailUrl = item.ThumbnailUrl;
            video.PublishedAt = item.PublishedAt;
            video.DurationSeconds = item.DurationSeconds;
        }

        channel.SyncStatus = ChannelSyncStatuses.Ready;
        channel.PendingRequestId = null;
        channel.NextPageStartIndex = result.NextStartIndex;
        channel.HasMoreVideos = result.HasMore;
        channel.LastSyncError = null;
        channel.UpdatedAt = envelope.OccurredAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FailPage(
        ExtractorEventEnvelopeV1 envelope,
        CancellationToken cancellationToken)
    {
        var failure = envelope.Payload.Deserialize<FetchChannelPageFailedPayloadV1>(JsonOptions)
            ?? throw new InvalidOperationException("Channel page failure payload is invalid.");
        var channel = await db.Channels.SingleOrDefaultAsync(
            x => x.Id == failure.ChannelId,
            cancellationToken);
        if (channel is null || channel.PendingRequestId != failure.RequestId)
        {
            return;
        }

        channel.SyncStatus = ChannelSyncStatuses.Failed;
        channel.PendingRequestId = null;
        channel.LastSyncError = failure.ErrorMessage;
        channel.UpdatedAt = envelope.OccurredAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
