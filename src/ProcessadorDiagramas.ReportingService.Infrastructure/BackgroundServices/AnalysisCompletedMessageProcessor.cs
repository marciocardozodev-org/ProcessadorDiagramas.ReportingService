using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProcessadorDiagramas.ReportingService.Application.Interfaces;
using ProcessadorDiagramas.ReportingService.Domain.Entities;
using ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport;
using ProcessadorDiagramas.ReportingService.Infrastructure.Data;
using ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.BackgroundServices;

public sealed class AnalysisCompletedMessageProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<AnalysisCompletedMessageProcessor> _logger;

    public AnalysisCompletedMessageProcessor(
        IServiceScopeFactory scopeFactory,
        IAmazonSQS sqsClient,
        ILogger<AnalysisCompletedMessageProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _sqsClient = sqsClient;
        _logger = logger;
    }

    public async Task ProcessAsync(Message message, string queueUrl, CancellationToken cancellationToken)
    {
        if (UploadOrchestratorCompletedEventParser.TryParse(message, out var uploadPayload))
        {
            try
            {
                var stored = await PersistUploadOrchestratorEventAsync(uploadPayload, message, queueUrl, cancellationToken);
                if (stored)
                    await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Erro ao processar evento upload-orchestrator-analysis-completed. requestId={RequestId} correlationId={CorrelationId}",
                    uploadPayload.RequestId,
                    uploadPayload.CorrelationId);
            }

            return;
        }

        if (!TryParseMessage(message, out var payload))
        {
            _logger.LogWarning("Mensagem ignorada por formato inválido. messageId={MessageId}", message.MessageId);
            await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            return;
        }

        if (string.Equals(payload.EventType, "AnalysisProcessingCompletedV2", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(payload.RawAiOutput))
            {
                _logger.LogWarning(
                    "Evento V2 inválido sem RawAiOutput. analysisProcessId={AnalysisProcessId} correlationId={CorrelationId} jobId={JobId}",
                    payload.AnalysisProcessId,
                    payload.CorrelationId,
                    payload.JobId);
                await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                return;
            }

            using var handleScope = _scopeFactory.CreateScope();
            var generationService = handleScope.ServiceProvider.GetRequiredService<IAnalysisReportGenerationService>();

            var sourceReference = !string.IsNullOrWhiteSpace(payload.SourceAnalysisReference)
                ? payload.SourceAnalysisReference
                : message.MessageId;

            var result = await generationService.GenerateAsync(
                payload.AnalysisProcessId,
                payload.RawAiOutput ?? string.Empty,
                sourceReference,
                cancellationToken);

            _logger.LogInformation(
                "Evento AnalysisProcessingCompletedV2 processado. analysisProcessId={AnalysisProcessId} correlationId={CorrelationId} jobId={JobId} reportStatus={ReportStatus}",
                payload.AnalysisProcessId,
                payload.CorrelationId,
                payload.JobId,
                result?.Status ?? "NotFound");

            await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            return;
        }

        if (!string.Equals(payload.EventType, "AnalysisProcessingCompleted", StringComparison.OrdinalIgnoreCase))
        {
            await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            return;
        }

        try
        {
            using var handleScope = _scopeFactory.CreateScope();
            var handler = handleScope.ServiceProvider.GetRequiredService<GetOrGenerateAnalysisReportQueryHandler>();

            var result = await handler.HandleAsync(
                new GetOrGenerateAnalysisReportQuery(payload.AnalysisProcessId),
                cancellationToken);

            _logger.LogInformation(
                "Evento AnalysisProcessingCompleted processado. analysisProcessId={AnalysisProcessId} correlationId={CorrelationId} jobId={JobId} reportStatus={ReportStatus}",
                payload.AnalysisProcessId,
                payload.CorrelationId,
                payload.JobId,
                result?.Status ?? "NotFound");

            await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao processar AnalysisProcessingCompleted. analysisProcessId={AnalysisProcessId} correlationId={CorrelationId} jobId={JobId}",
                payload.AnalysisProcessId,
                payload.CorrelationId,
                payload.JobId);
        }
    }

    private static bool TryParseMessage(Message message, out AnalysisCompletedEventPayload payload)
        => AnalysisCompletedEventParser.TryParse(message, out payload);

    private async Task<bool> PersistUploadOrchestratorEventAsync(
        UploadOrchestratorCompletedPayload payload,
        Message message,
        string queueUrl,
        CancellationToken cancellationToken)
    {
        if (!IsValidUploadPayload(payload))
        {
            _logger.LogWarning(
                "Mensagem upload-orchestrator inválida. requestId={RequestId} correlationId={CorrelationId}",
                payload.RequestId,
                payload.CorrelationId);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        var alreadyProcessed = await dbContext.ProcessedInboxMessages
            .AnyAsync(i => i.CorrelationId == payload.CorrelationId, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Mensagem duplicada ignorada por dedupe de correlationId. requestId={RequestId} correlationId={CorrelationId}",
                payload.RequestId,
                payload.CorrelationId);
            return true;
        }

        var metadataResponse = await s3Client.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = payload.S3ArtifactBucket,
                Key = payload.S3ArtifactKey
            },
            cancellationToken);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await dbContext.Reports
            .FirstOrDefaultAsync(r => r.RequestId == payload.RequestId, cancellationToken);

        if (existing is null)
        {
            existing = ReportRecord.Create(
                payload.RequestId,
                payload.CorrelationId,
                payload.S3ArtifactBucket,
                payload.S3ArtifactKey,
                payload.Status,
                metadataResponse.ETag,
                metadataResponse.Headers.ContentType,
                metadataResponse.Headers.ContentLength);
            await dbContext.Reports.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.UpdateArtifact(
                payload.CorrelationId,
                payload.S3ArtifactBucket,
                payload.S3ArtifactKey,
                payload.Status,
                metadataResponse.ETag,
                metadataResponse.Headers.ContentType,
                metadataResponse.Headers.ContentLength);
        }

        var inbox = ProcessedInboxMessage.Create(
            payload.CorrelationId,
            payload.RequestId,
            queueUrl,
            message.MessageId ?? string.Empty);
        await dbContext.ProcessedInboxMessages.AddAsync(inbox, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Evento upload-orchestrator persistido com sucesso. requestId={RequestId} correlationId={CorrelationId} s3={Bucket}/{Key}",
            payload.RequestId,
            payload.CorrelationId,
            payload.S3ArtifactBucket,
            payload.S3ArtifactKey);

        return true;
    }

    private static bool IsValidUploadPayload(UploadOrchestratorCompletedPayload payload)
        => !string.IsNullOrWhiteSpace(payload.RequestId)
           && !string.IsNullOrWhiteSpace(payload.CorrelationId)
           && !string.IsNullOrWhiteSpace(payload.S3ArtifactBucket)
           && !string.IsNullOrWhiteSpace(payload.S3ArtifactKey)
           && !string.IsNullOrWhiteSpace(payload.Status);
}