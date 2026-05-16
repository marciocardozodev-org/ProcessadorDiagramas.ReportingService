namespace ProcessadorDiagramas.ReportingService.Domain.Entities;

public sealed class ProcessedInboxMessage
{
    public Guid Id { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string RequestId { get; private set; } = string.Empty;
    public string SourceQueue { get; private set; } = string.Empty;
    public string MessageId { get; private set; } = string.Empty;
    public DateTime ProcessedAt { get; private set; }

    private ProcessedInboxMessage()
    {
    }

    public static ProcessedInboxMessage Create(
        string correlationId,
        string requestId,
        string sourceQueue,
        string messageId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId cannot be empty.", nameof(requestId));

        return new ProcessedInboxMessage
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId.Trim(),
            RequestId = requestId.Trim(),
            SourceQueue = sourceQueue?.Trim() ?? string.Empty,
            MessageId = messageId?.Trim() ?? string.Empty,
            ProcessedAt = DateTime.UtcNow
        };
    }
}
