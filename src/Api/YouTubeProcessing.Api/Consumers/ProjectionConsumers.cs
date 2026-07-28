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

        await db.SaveChangesAsync(context.CancellationToken);
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
