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
                    UpdatedAt = message.CompletedAt
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
                LastProcessedAt = message.CompletedAt
            });
            return;
        }

        if (message.CompletedAt < video.FirstProcessedAt)
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
