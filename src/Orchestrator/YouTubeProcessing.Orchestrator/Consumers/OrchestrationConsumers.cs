using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using YouTubeProcessing.Contracts;
using YouTubeProcessing.Orchestrator.Data;

namespace YouTubeProcessing.Orchestrator.Consumers;

public sealed class VideoAnalysisRequestedConsumer(
    OrchestratorDbContext db,
    ISendEndpointProvider sender) : IConsumer<VideoAnalysisRequestedV1>
{
    public async Task Consume(ConsumeContext<VideoAnalysisRequestedV1> context)
    {
        var message = context.Message;
        if (await db.Processes.AnyAsync(x => x.JobId == message.JobId, context.CancellationToken))
        {
            return;
        }

        db.Processes.Add(new AnalysisProcess
        {
            JobId = message.JobId,
            AnalysisId = message.AnalysisId,
            Stage = JobStages.Extracting,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await context.Publish(
            new JobStageChangedV1(
                message.JobId,
                message.AnalysisId,
                JobStatuses.Running,
                JobStages.Extracting,
                10,
                DateTimeOffset.UtcNow),
            publish => publish.CorrelationId = message.JobId);

        var endpoint = await sender.GetSendEndpoint(
            new Uri($"exchange:{QueueNames.ExtractorCommands}"));
        await endpoint.Send(
            new IntegrationEnvelopeV1<ExtractVideoPayloadV1>(
                "1",
                NewId.NextGuid(),
                message.JobId,
                context.MessageId,
                IntegrationMessageTypes.ExtractVideo,
                DateTimeOffset.UtcNow,
                new ExtractVideoPayloadV1(
                    message.JobId,
                    message.AnalysisId,
                    message.VideoId,
                    message.OriginalInput,
                    message.PreferredLanguage)),
            send =>
            {
                send.CorrelationId = message.JobId;
            });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class ExtractorEventConsumer(
    OrchestratorDbContext db,
    IOptions<WorkflowOptions> workflowOptions,
    ILogger<ExtractorEventConsumer> logger) : IConsumer<ExtractorEventEnvelopeV1>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task Consume(ConsumeContext<ExtractorEventEnvelopeV1> context)
    {
        var envelope = context.Message;
        if (envelope.SchemaVersion != "1")
        {
            throw new InvalidOperationException(
                $"Unsupported extractor schema version '{envelope.SchemaVersion}'.");
        }

        var process = await db.Processes.SingleOrDefaultAsync(
            x => x.JobId == envelope.CorrelationId,
            context.CancellationToken);
        if (process is null)
        {
            logger.LogWarning(
                "Ignoring extractor event {MessageId}; process {JobId} was not found",
                envelope.MessageId,
                envelope.CorrelationId);
            return;
        }

        if (process.Stage != JobStages.Extracting)
        {
            return;
        }

        switch (envelope.MessageType)
        {
            case IntegrationMessageTypes.ExtractVideoCompleted:
                await CompleteExtraction(context, process, envelope);
                break;
            case IntegrationMessageTypes.ExtractVideoFailed:
                await FailExtraction(context, process, envelope);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported extractor message type '{envelope.MessageType}'.");
        }
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private async Task CompleteExtraction(
        ConsumeContext<ExtractorEventEnvelopeV1> context,
        AnalysisProcess process,
        ExtractorEventEnvelopeV1 envelope)
    {
        var result = envelope.Payload.Deserialize<ExtractVideoCompletedPayloadV1>(JsonOptions)
            ?? throw new InvalidOperationException("Extractor completion payload is invalid.");
        if (result.JobId != process.JobId || result.AnalysisId != process.AnalysisId)
        {
            throw new InvalidOperationException("Extractor payload correlation does not match its envelope.");
        }

        var completion = ExtractionCompletionPlan.Create(
            result,
            workflowOptions.Value.AnalysisEnabled,
            DateTimeOffset.UtcNow);

        process.Stage = completion.NextStage;
        process.ExtractionJson = JsonSerializer.Serialize(result, JsonOptions);
        process.UpdatedAt = DateTimeOffset.UtcNow;

        if (completion.StageChange is not null)
        {
            await context.Publish(
                completion.StageChange,
                publish => publish.CorrelationId = process.JobId);
        }
        if (completion.AnalysisRequest is not null)
        {
            await context.Publish(
                completion.AnalysisRequest,
                publish => publish.CorrelationId = process.JobId);
        }
        if (completion.Completion is not null)
        {
            await context.Publish(
                completion.Completion,
                publish => publish.CorrelationId = process.JobId);
        }
    }

    private static async Task FailExtraction(
        ConsumeContext<ExtractorEventEnvelopeV1> context,
        AnalysisProcess process,
        ExtractorEventEnvelopeV1 envelope)
    {
        var failure = envelope.Payload.Deserialize<ExtractVideoFailedPayloadV1>(JsonOptions)
            ?? throw new InvalidOperationException("Extractor failure payload is invalid.");
        process.Stage = JobStages.Failed;
        process.UpdatedAt = DateTimeOffset.UtcNow;
        await context.Publish(
            new VideoAnalysisFailedV1(
                process.JobId,
                process.AnalysisId,
                JobStages.Extracting,
                failure.ErrorCode,
                failure.ErrorMessage,
                DateTimeOffset.UtcNow),
            publish => publish.CorrelationId = process.JobId);
    }
}

public sealed class TranscriptAnalyzedConsumer(OrchestratorDbContext db)
    : IConsumer<TranscriptAnalyzedV1>
{
    public async Task Consume(ConsumeContext<TranscriptAnalyzedV1> context)
    {
        var message = context.Message;
        var process = await db.Processes.SingleOrDefaultAsync(
            x => x.JobId == message.JobId,
            context.CancellationToken);
        if (process is null || process.Stage != JobStages.Analyzing)
        {
            return;
        }

        var extraction = JsonSerializer.Deserialize<ExtractVideoCompletedPayloadV1>(
            process.ExtractionJson
                ?? throw new InvalidOperationException("Extraction state is missing."));
        if (extraction is null)
        {
            throw new InvalidOperationException("Extraction state is invalid.");
        }

        process.Stage = JobStages.Completed;
        process.UpdatedAt = message.CompletedAt;
        await context.Publish(
            new VideoAnalysisCompletedV1(
                message.JobId,
                message.AnalysisId,
                extraction.Video,
                extraction.Transcript,
                extraction.OriginalCaption,
                extraction.TranscriptSegments,
                extraction.Thumbnail,
                message.Summary,
                message.Subjects,
                message.Model,
                message.PromptVersion,
                message.CompletedAt),
            publish => publish.CorrelationId = message.JobId);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class TranscriptAnalysisFailedConsumer(OrchestratorDbContext db)
    : IConsumer<TranscriptAnalysisFailedV1>
{
    public async Task Consume(ConsumeContext<TranscriptAnalysisFailedV1> context)
    {
        var message = context.Message;
        var process = await db.Processes.SingleOrDefaultAsync(
            x => x.JobId == message.JobId,
            context.CancellationToken);
        if (process is null || process.Stage != JobStages.Analyzing)
        {
            return;
        }

        process.Stage = JobStages.Failed;
        process.UpdatedAt = message.FailedAt;
        await context.Publish(
            new VideoAnalysisFailedV1(
                message.JobId,
                message.AnalysisId,
                JobStages.Analyzing,
                message.ErrorCode,
                message.ErrorMessage,
                message.FailedAt),
            publish => publish.CorrelationId = message.JobId);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
