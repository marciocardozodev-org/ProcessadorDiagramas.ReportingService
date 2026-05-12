using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProcessadorDiagramas.ReportingService.Application.Interfaces;
using ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport;
using ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

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
}