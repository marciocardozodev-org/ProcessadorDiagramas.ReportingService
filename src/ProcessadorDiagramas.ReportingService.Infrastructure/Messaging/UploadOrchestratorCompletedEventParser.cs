using System.Text.Json;
using Amazon.SQS.Model;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

public static class UploadOrchestratorCompletedEventParser
{
    public static bool TryParse(Message message, out UploadOrchestratorCompletedPayload payload)
    {
        payload = default;

        var rawBody = message.Body;
        if (!TryExtractPayload(rawBody, out var extractedBody))
            extractedBody = rawBody;

        try
        {
            using var doc = JsonDocument.Parse(extractedBody);
            var root = doc.RootElement;

            var requestId = ReadString(root, "requestId") ?? ReadString(root, "RequestId");
            var correlationId = ReadString(root, "correlationId") ?? ReadString(root, "CorrelationId");
            var bucket = ReadString(root, "s3ArtifactBucket") ?? ReadString(root, "S3ArtifactBucket");
            var key = ReadString(root, "s3ArtifactKey") ?? ReadString(root, "S3ArtifactKey");
            var status = ReadString(root, "status") ?? ReadString(root, "Status");

            if (string.IsNullOrWhiteSpace(requestId)
                || string.IsNullOrWhiteSpace(correlationId)
                || string.IsNullOrWhiteSpace(bucket)
                || string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            payload = new UploadOrchestratorCompletedPayload(
                requestId.Trim(),
                correlationId.Trim(),
                bucket.Trim(),
                key.Trim(),
                status.Trim());

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractPayload(string body, out string payload)
    {
        payload = body;

        try
        {
            using var envelopeDoc = JsonDocument.Parse(body);
            var envelopeRoot = envelopeDoc.RootElement;

            if (!envelopeRoot.TryGetProperty("Message", out var messageElement))
                return false;

            payload = messageElement.GetString() ?? body;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node) || node.ValueKind != JsonValueKind.String)
            return null;

        return node.GetString();
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

public readonly record struct UploadOrchestratorCompletedPayload(
    string RequestId,
    string CorrelationId,
    string S3ArtifactBucket,
    string S3ArtifactKey,
    string Status);
