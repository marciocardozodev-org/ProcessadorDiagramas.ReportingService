using Amazon.SQS.Model;
using FluentAssertions;
using System.Text.Json;
using ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

namespace ProcessadorDiagramas.ReportingService.Tests.Infrastructure.Messaging;

public sealed class AnalysisCompletedEventParserTests
{
    [Fact]
    public void TryParse_DirectV2Payload_ReturnsPayload()
    {
        var analysisProcessId = Guid.NewGuid();
        var jobId = Guid.NewGuid().ToString();
        var directPayload = JsonSerializer.Serialize(new
        {
            EventType = "AnalysisProcessingCompletedV2",
            AnalysisProcessId = analysisProcessId,
            RawAiOutput = "{\"components\":\"[API]\"}",
            SourceAnalysisReference = jobId,
            CorrelationId = "corr-123"
        });

        var message = new Message
        {
            Body = directPayload
        };

        var parsed = AnalysisCompletedEventParser.TryParse(message, out var payload);

        parsed.Should().BeTrue();
        payload.EventType.Should().Be("AnalysisProcessingCompletedV2");
        payload.AnalysisProcessId.Should().Be(analysisProcessId);
        payload.RawAiOutput.Should().Contain("API");
        payload.SourceAnalysisReference.Should().Be(jobId);
        payload.CorrelationId.Should().Be("corr-123");
        payload.JobId.Should().Be(jobId);
    }

    [Fact]
    public void TryParse_SnsEnvelopeWithMessageAttributes_ReturnsPayload()
    {
        var analysisProcessId = Guid.NewGuid();
        var messageId = Guid.NewGuid().ToString();
        var innerPayload = JsonSerializer.Serialize(new
        {
            EventType = "AnalysisProcessingCompletedV2",
            AnalysisProcessId = analysisProcessId,
            RawAiOutput = "{\"risks\":\"[SPOF]\"}",
            SourceAnalysisReference = messageId,
            CorrelationId = "corr-456"
        });

        var snsEnvelope = JsonSerializer.Serialize(new
        {
            Type = "Notification",
            Message = innerPayload,
            MessageAttributes = new Dictionary<string, object>
            {
                ["eventType"] = new { Type = "String", Value = "AnalysisProcessingCompletedV2" }
            }
        });

        var message = new Message
        {
            Body = snsEnvelope,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new MessageAttributeValue { DataType = "String", StringValue = "AnalysisProcessingCompletedV2" }
            }
        };

        var parsed = AnalysisCompletedEventParser.TryParse(message, out var payload);

        parsed.Should().BeTrue();
        payload.EventType.Should().Be("AnalysisProcessingCompletedV2");
        payload.AnalysisProcessId.Should().Be(analysisProcessId);
        payload.RawAiOutput.Should().Contain("SPOF");
        payload.SourceAnalysisReference.Should().Be(messageId);
        payload.CorrelationId.Should().Be("corr-456");
    }

    [Fact]
    public void TryParse_LegacyPayload_ReturnsPayloadWithoutRawOutput()
    {
        var analysisProcessId = Guid.NewGuid();
        var message = new Message
        {
            Body = JsonSerializer.Serialize(new
            {
                EventType = "AnalysisProcessingCompleted",
                AnalysisProcessId = analysisProcessId,
                CorrelationId = "corr-789"
            })
        };

        var parsed = AnalysisCompletedEventParser.TryParse(message, out var payload);

        parsed.Should().BeTrue();
        payload.EventType.Should().Be("AnalysisProcessingCompleted");
        payload.AnalysisProcessId.Should().Be(analysisProcessId);
        payload.RawAiOutput.Should().BeNull();
        payload.SourceAnalysisReference.Should().BeNull();
        payload.CorrelationId.Should().Be("corr-789");
    }

    [Fact]
    public void TryParse_InvalidPayload_ReturnsFalse()
    {
        var message = new Message { Body = "not-json" };

        var parsed = AnalysisCompletedEventParser.TryParse(message, out var payload);

        parsed.Should().BeFalse();
        payload.EventType.Should().BeNullOrEmpty();
        payload.AnalysisProcessId.Should().Be(Guid.Empty);
    }
}