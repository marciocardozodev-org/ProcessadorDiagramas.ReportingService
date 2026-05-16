namespace ProcessadorDiagramas.ReportingService.Domain.Entities;

public sealed class ReportRecord
{
    public Guid Id { get; private set; }
    public string RequestId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string S3ArtifactBucket { get; private set; } = string.Empty;
    public string S3ArtifactKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string? ETag { get; private set; }
    public string? ContentType { get; private set; }
    public long? ContentLength { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ReportRecord()
    {
    }

    public static ReportRecord Create(
        string requestId,
        string correlationId,
        string bucket,
        string key,
        string status,
        string? eTag,
        string? contentType,
        long? contentLength)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId cannot be empty.", nameof(requestId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("S3 bucket cannot be empty.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("S3 key cannot be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status cannot be empty.", nameof(status));

        var now = DateTime.UtcNow;
        return new ReportRecord
        {
            Id = Guid.NewGuid(),
            RequestId = requestId.Trim(),
            CorrelationId = correlationId.Trim(),
            S3ArtifactBucket = bucket.Trim(),
            S3ArtifactKey = key.Trim(),
            Status = status.Trim(),
            ETag = eTag,
            ContentType = contentType,
            ContentLength = contentLength,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateArtifact(
        string correlationId,
        string bucket,
        string key,
        string status,
        string? eTag,
        string? contentType,
        long? contentLength)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("S3 bucket cannot be empty.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("S3 key cannot be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status cannot be empty.", nameof(status));

        CorrelationId = correlationId.Trim();
        S3ArtifactBucket = bucket.Trim();
        S3ArtifactKey = key.Trim();
        Status = status.Trim();
        ETag = eTag;
        ContentType = contentType;
        ContentLength = contentLength;
        UpdatedAt = DateTime.UtcNow;
    }
}
