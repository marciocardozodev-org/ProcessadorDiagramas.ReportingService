using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;
using ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.BackgroundServices;

public sealed class AnalysisCompletedConsumerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisCompletedConsumerBackgroundService> _logger;
    private readonly MessagingOptions _messagingOptions;

    public AnalysisCompletedConsumerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MessagingOptions> messagingOptions,
        ILogger<AnalysisCompletedConsumerBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _messagingOptions = messagingOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao consumir fila de AnalysisProcessingCompleted.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _messagingOptions.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task PollQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueResolver = scope.ServiceProvider.GetRequiredService<SqsQueueUrlResolver>();
        var sqsClient = scope.ServiceProvider.GetRequiredService<IAmazonSQS>();
        var processor = scope.ServiceProvider.GetRequiredService<AnalysisCompletedMessageProcessor>();
        var queueOptions = scope.ServiceProvider.GetRequiredService<IOptions<QueuesOptions>>().Value;

        var queueUrl = !string.IsNullOrWhiteSpace(queueOptions.AnalysisCompletedQueueUrl)
            ? queueOptions.AnalysisCompletedQueueUrl
            : await queueResolver.ResolveQueueUrlAsync(queueOptions.AnalysisCompletedQueueName, cancellationToken);

        var response = await sqsClient.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = Math.Clamp(_messagingOptions.MaxNumberOfMessages, 1, 10),
                WaitTimeSeconds = Math.Clamp(_messagingOptions.WaitTimeSeconds, 1, 20),
                MessageAttributeNames = ["All"]
            },
            cancellationToken);

        if (response.Messages is null || response.Messages.Count == 0)
            return;

        foreach (var message in response.Messages)
        {
            await processor.ProcessAsync(message, queueUrl, cancellationToken);
        }
    }
}
