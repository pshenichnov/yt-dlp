using Amazon.S3;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YouTubeProcessing.Api.Data;
using YouTubeProcessing.Api.Infrastructure;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Api.Endpoints;

public static class VideoAnalysisEndpoints
{
    public static IEndpointRouteBuilder MapVideoAnalysisEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");
        group.MapPost("/video-analyses", CreateAnalysis);
        group.MapGet("/jobs/{jobId:guid}", GetJob);
        group.MapGet("/video-analyses/{analysisId:guid}", GetAnalysis);
        group.MapGet("/artifacts/{artifactId:guid}", DownloadArtifact);
        return endpoints;
    }

    private static async Task<IResult> CreateAnalysis(
        CreateVideoAnalysisRequest request,
        HttpContext httpContext,
        ApiDbContext db,
        IPublishEndpoint publisher,
        IOptions<PlatformOptions> options,
        CancellationToken cancellationToken)
    {
        if (!VideoInputCanonicalizer.TryCanonicalize(
                request.Video,
                options.Value.MaxInputLength,
                out var videoId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["video"] = ["Provide an 11-character YouTube video ID or a supported youtube.com/youtu.be HTTPS URL."]
            });
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["The Idempotency-Key header cannot exceed 200 characters."]
            });
        }

        if (idempotencyKey.Length > 0)
        {
            var existing = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                var existingJob = await db.Jobs.AsNoTracking()
                    .SingleAsync(x => x.Id == existing.JobId, cancellationToken);
                return Results.Accepted(
                    $"/api/v1/jobs/{existing.JobId}",
                    new CreateVideoAnalysisResponse(
                        existing.JobId,
                        existingJob.AnalysisId,
                        existingJob.Status,
                        existingJob.CreatedAt,
                        $"/api/v1/jobs/{existing.JobId}",
                        $"/api/v1/video-analyses/{existingJob.AnalysisId}"));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var jobId = NewId.NextGuid();
        var analysisId = NewId.NextGuid();

        db.Jobs.Add(new JobEntity
        {
            Id = jobId,
            AnalysisId = analysisId,
            Status = JobStatuses.Queued,
            Stage = JobStages.Queued,
            ProgressPercent = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Analyses.Add(new AnalysisEntity
        {
            Id = analysisId,
            JobId = jobId,
            VideoId = videoId,
            OriginalInput = request.Video.Trim(),
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? null
                : request.PreferredLanguage.Trim(),
            CreatedAt = now
        });

        if (idempotencyKey.Length > 0)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = idempotencyKey,
                JobId = jobId,
                CreatedAt = now,
                ExpiresAt = now.AddHours(24)
            });
        }

        await publisher.Publish(
            new VideoAnalysisRequestedV1(
                jobId,
                analysisId,
                videoId,
                request.Video.Trim(),
                request.PreferredLanguage,
                now),
            context => context.CorrelationId = jobId,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Accepted(
            $"/api/v1/jobs/{jobId}",
            new CreateVideoAnalysisResponse(
                jobId,
                analysisId,
                JobStatuses.Queued,
                now,
                $"/api/v1/jobs/{jobId}",
                $"/api/v1/video-analyses/{analysisId}"));
    }

    private static async Task<IResult> GetJob(
        Guid jobId,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => new
            {
                x.Id,
                x.AnalysisId,
                x.Status,
                x.Stage,
                x.ProgressPercent,
                x.ErrorCode,
                x.ErrorMessage,
                x.CreatedAt,
                x.UpdatedAt,
                x.CompletedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new JobResponse(
                job.Id,
                job.AnalysisId,
                "videoAnalysis",
                job.Status,
                job.Stage,
                job.ProgressPercent,
                job.CreatedAt,
                job.Status == JobStatuses.Queued ? null : job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt,
                job.ErrorCode is null
                    ? null
                    : new JobErrorResponse(job.ErrorCode, job.ErrorMessage ?? "Processing failed.", false)));
    }

    private static async Task<IResult> GetAnalysis(
        Guid analysisId,
        ApiDbContext db,
        CancellationToken cancellationToken)
    {
        var analysis = await db.Analyses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == analysisId, cancellationToken);
        if (analysis is null)
        {
            return Results.NotFound();
        }

        var job = await db.Jobs.AsNoTracking()
            .SingleAsync(x => x.Id == analysis.JobId, cancellationToken);
        if (job.Status != JobStatuses.Completed)
        {
            return Results.Accepted(
                $"/api/v1/jobs/{job.Id}",
                new
                {
                    jobId = job.Id,
                    analysisId = analysis.Id,
                    status = job.Status,
                    stage = job.Stage,
                    statusUrl = $"/api/v1/jobs/{job.Id}"
                });
        }

        var artifacts = await db.Artifacts.AsNoTracking()
            .Where(x => x.AnalysisId == analysisId)
            .OrderBy(x => x.Kind)
            .Select(x => new ArtifactResponse(
                x.Id,
                x.Kind,
                x.ContentType,
                x.SizeBytes,
                x.Sha256,
                $"/api/v1/artifacts/{x.Id}"))
            .ToListAsync(cancellationToken);

        var subjects = analysis.SubjectsJson is null
            ? []
            : JsonSerializer.Deserialize<string[]>(analysis.SubjectsJson) ?? [];

        var transcript = artifacts.FirstOrDefault(x => x.Kind == "transcript");
        var thumbnail = artifacts.FirstOrDefault(x => x.Kind == "thumbnail");
        return Results.Ok(new VideoAnalysisResponse(
            analysis.Id,
            analysis.JobId,
            new VideoResponse(
                analysis.VideoId,
                analysis.WebpageUrl,
                analysis.Title,
                analysis.ChannelId,
                analysis.ChannelName,
                analysis.PublishedAt,
                analysis.DurationSeconds,
                analysis.ViewCount,
                analysis.LikeCount),
            analysis.Summary ?? string.Empty,
            subjects,
            transcript is null
                ? null
                : new TranscriptResponse(
                    analysis.PreferredLanguage ?? "en",
                    "youtubeCaption",
                    transcript.DownloadUrl),
            thumbnail is null
                ? null
                : new ThumbnailResponse(thumbnail.ContentType, thumbnail.DownloadUrl),
            analysis.AnalysisModel is null
                ? null
                : new AnalysisDetailsResponse(
                    "ollama",
                    analysis.AnalysisModel,
                    analysis.PromptVersion,
                    analysis.CompletedAt)));
    }

    private static async Task<IResult> DownloadArtifact(
        Guid artifactId,
        ApiDbContext db,
        IAmazonS3 s3,
        IOptions<PlatformOptions> options,
        CancellationToken cancellationToken)
    {
        var artifact = await db.Artifacts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == artifactId, cancellationToken);
        if (artifact is null)
        {
            return Results.NotFound();
        }

        var response = await s3.GetObjectAsync(
            options.Value.Blob.Bucket,
            artifact.ObjectKey,
            cancellationToken);
        return Results.Stream(
            response.ResponseStream,
            artifact.ContentType,
            Path.GetFileName(artifact.ObjectKey),
            enableRangeProcessing: true);
    }
}

public sealed record CreateVideoAnalysisRequest(string Video, string? PreferredLanguage);
public sealed record CreateVideoAnalysisResponse(
    Guid JobId,
    Guid AnalysisId,
    string Status,
    DateTimeOffset SubmittedAt,
    string StatusUrl,
    string ResultUrl);
public sealed record JobResponse(
    Guid JobId,
    Guid AnalysisId,
    string Type,
    string Status,
    string Stage,
    int Progress,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    JobErrorResponse? Error);
public sealed record JobErrorResponse(string Code, string Message, bool Retryable);
public sealed record ArtifactResponse(
    Guid ArtifactId,
    string Kind,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string DownloadUrl);
public sealed record VideoAnalysisResponse(
    Guid AnalysisId,
    Guid JobId,
    VideoResponse Video,
    string Description,
    IReadOnlyList<string> Subjects,
    TranscriptResponse? Transcript,
    ThumbnailResponse? Thumbnail,
    AnalysisDetailsResponse? Analysis);
public sealed record VideoResponse(
    string VideoId,
    string? Url,
    string? Title,
    string? ChannelId,
    string? ChannelName,
    DateTimeOffset? UploadDate,
    long? DurationSeconds,
    long? ViewCount,
    long? LikeCount);
public sealed record TranscriptResponse(string Language, string Source, string ArtifactUrl);
public sealed record ThumbnailResponse(string ContentType, string ArtifactUrl);
public sealed record AnalysisDetailsResponse(
    string Provider,
    string? Model,
    string? PromptVersion,
    DateTimeOffset? CreatedAt);
