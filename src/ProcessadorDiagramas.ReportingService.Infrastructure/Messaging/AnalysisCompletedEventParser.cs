using System.Text.Json;
using Amazon.SQS.Model;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

public static class AnalysisCompletedEventParser
{
    public static bool TryParse(Message message, out AnalysisCompletedEventPayload payload)
    {
        payload = default;

        var rawBody = message.Body;
        string? snsEventType = null;

        if (!TryExtractPayload(rawBody, out var extractedBody, out snsEventType))
            extractedBody = rawBody;

        try
        {
            using var doc = JsonDocument.Parse(extractedBody);
            var root = doc.RootElement;

            var eventType = ResolveEventType(message, root, snsEventType);
            var analysisProcessId = ReadGuid(root, "AnalysisProcessId")
                ?? ReadGuid(root, "processId");
            var rawAiOutput = ReadString(root, "RawAiOutput");
            var sourceAnalysisReference = ReadString(root, "SourceAnalysisReference");
            var correlationId = ReadString(root, "CorrelationId") ?? string.Empty;
            var jobId = (sourceAnalysisReference ?? string.Empty).Trim();

            if (analysisProcessId is null)
                return false;

            payload = new AnalysisCompletedEventPayload(
                eventType ?? string.Empty,
                analysisProcessId.Value,
                rawAiOutput,
                sourceAnalysisReference,
                correlationId,
                jobId);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractPayload(string body, out string payload, out string? snsEventType)
    {
        payload = body;
        snsEventType = null;

        try
        {
            using var envelopeDoc = JsonDocument.Parse(body);
            var envelopeRoot = envelopeDoc.RootElement;

            if (!envelopeRoot.TryGetProperty("Message", out var messageElement))
                return false;

            payload = messageElement.GetString() ?? body;

            if (envelopeRoot.TryGetProperty("MessageAttributes", out var attrs) &&
                attrs.TryGetProperty("eventType", out var eventTypeAttr) &&
                eventTypeAttr.TryGetProperty("Value", out var valueNode))
            {
                snsEventType = valueNode.GetString();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveEventType(Message message, JsonElement payloadRoot, string? snsEventType)
    {
        if (message.MessageAttributes is not null
            && message.MessageAttributes.TryGetValue("eventType", out var attr)
            && !string.IsNullOrWhiteSpace(attr.StringValue))
            return attr.StringValue;

        if (!string.IsNullOrWhiteSpace(snsEventType))
            return snsEventType;

        return ReadString(payloadRoot, "EventType");
    }

    private static Guid? ReadGuid(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var node))
            return null;

        return node.ValueKind switch
        {
            JsonValueKind.String when Guid.TryParse(node.GetString(), out var parsed) => parsed,
            _ => null
        };
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

public readonly record struct AnalysisCompletedEventPayload(
    string EventType,
    Guid AnalysisProcessId,
    string? RawAiOutput,
    string? SourceAnalysisReference,
    string CorrelationId,
    string JobId);