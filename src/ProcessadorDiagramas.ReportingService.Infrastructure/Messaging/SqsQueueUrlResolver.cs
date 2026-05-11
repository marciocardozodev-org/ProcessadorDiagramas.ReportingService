using Amazon.SQS;
using Amazon.SQS.Model;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

public sealed class SqsQueueUrlResolver
{
    private readonly IAmazonSQS _sqsClient;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public SqsQueueUrlResolver(IAmazonSQS sqsClient)
    {
        _sqsClient = sqsClient;
    }

    public async Task<string> ResolveQueueUrlAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(queueName, out var cached))
            return cached;

        var response = await _sqsClient.GetQueueUrlAsync(
            new GetQueueUrlRequest { QueueName = queueName },
            cancellationToken);

        _cache[queueName] = response.QueueUrl;
        return response.QueueUrl;
    }
}
