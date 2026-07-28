using Amazon.S3;
using MassTransit;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using YouTubeProcessing.Analyzer.Analysis;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Analyzer.Consumers;

public sealed class AnalyzeTranscriptConsumer(
    IAmazonS3 s3,
    IOptions<AnalyzerOptions> options,
    ITranscriptAnalyzer analyzer,
    ILogger<AnalyzeTranscriptConsumer> logger) : IConsumer<AnalyzeTranscriptV1>
{
    public async Task Consume(ConsumeContext<AnalyzeTranscriptV1> context)
    {
        var message = context.Message;
        try
        {
            using var response = await s3.GetObjectAsync(
                options.Value.Blob.Bucket,
                message.Transcript.ObjectKey,
                context.CancellationToken);
            using var reader = new StreamReader(
                response.ResponseStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var transcript = await reader.ReadToEndAsync(context.CancellationToken);

            if (transcript.Length > options.Value.MaxTranscriptCharacters)
            {
                throw new AnalysisException(
                    "transcript_too_large",
                    $"Transcript exceeds {options.Value.MaxTranscriptCharacters} characters.",
                    false);
            }

            var checksum = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(transcript))).ToLowerInvariant();
            if (!checksum.Equals(message.Transcript.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new AnalysisException(
                    "transcript_checksum_mismatch",
                    "Downloaded transcript did not match the extractor checksum.",
                    false);
            }

            var result = await analyzer.AnalyzeAsync(
                message.Video,
                transcript,
                context.CancellationToken);
            await context.Publish(
                new TranscriptAnalyzedV1(
                    message.JobId,
                    message.AnalysisId,
                    result.Summary,
                    result.Subjects,
                    result.Model,
                    result.PromptVersion,
                    DateTimeOffset.UtcNow),
                publish => publish.CorrelationId = message.JobId);
        }
        catch (AnalysisException exception)
        {
            await PublishFailure(context, message, exception.Code, exception.Message, exception.IsTransient);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or AmazonS3Exception)
        {
            logger.LogWarning(exception, "Transient analysis failure for job {JobId}", message.JobId);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Analysis failed for job {JobId}", message.JobId);
            await PublishFailure(
                context,
                message,
                "analysis_failed",
                "The transcript analysis provider could not produce a valid result.",
                false);
        }
    }

    private static Task PublishFailure(
        ConsumeContext<AnalyzeTranscriptV1> context,
        AnalyzeTranscriptV1 message,
        string code,
        string errorMessage,
        bool isTransient) =>
        context.Publish(
            new TranscriptAnalysisFailedV1(
                message.JobId,
                message.AnalysisId,
                code,
                errorMessage,
                isTransient,
                DateTimeOffset.UtcNow),
            publish => publish.CorrelationId = message.JobId);
}

public sealed class AnalysisException(
    string code,
    string message,
    bool isTransient) : Exception(message)
{
    public string Code { get; } = code;
    public bool IsTransient { get; } = isTransient;
}
