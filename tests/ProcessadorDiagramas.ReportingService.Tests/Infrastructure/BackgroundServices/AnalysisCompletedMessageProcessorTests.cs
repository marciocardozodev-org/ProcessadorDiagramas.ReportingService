using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProcessadorDiagramas.ReportingService.Application.Interfaces;
using ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport;
using ProcessadorDiagramas.ReportingService.Infrastructure.BackgroundServices;

namespace ProcessadorDiagramas.ReportingService.Tests.Infrastructure.BackgroundServices;

public sealed class AnalysisCompletedMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_V2Message_GeneratesReportAndDeletesMessage()
    {
        var analysisProcessId = Guid.NewGuid();
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/analysis-completed";
        var message = new Message
        {
            MessageId = "msg-1",
            ReceiptHandle = "rh-1",
            Body = $$"""{"EventType":"AnalysisProcessingCompletedV2","AnalysisProcessId":"{{analysisProcessId}}","RawAiOutput":"{\"components\":\"[API]\"}","SourceAnalysisReference":"job-123","CorrelationId":"corr-1"}"""
        };

        var generationServiceMock = new Mock<IAnalysisReportGenerationService>();
        generationServiceMock
            .Setup(s => s.GenerateAsync(analysisProcessId, It.IsAny<string>(), "job-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport.GetOrGenerateAnalysisReportResponse(
                Guid.NewGuid(), analysisProcessId, "Generated", "components", "risks", "recs", "job-123", 1, null, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow));

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IAnalysisReportGenerationService)))
            .Returns(generationServiceMock.Object);
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        scope.Setup(s => s.Dispose());

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var sqsMock = new Mock<IAmazonSQS>();
        sqsMock
            .Setup(c => c.DeleteMessageAsync(queueUrl, "rh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var processor = new AnalysisCompletedMessageProcessor(
            scopeFactory.Object,
            sqsMock.Object,
            NullLogger<AnalysisCompletedMessageProcessor>.Instance);

        await processor.ProcessAsync(message, queueUrl, default);

        generationServiceMock.Verify(
            s => s.GenerateAsync(analysisProcessId, It.IsAny<string>(), "job-123", It.IsAny<CancellationToken>()),
            Times.Once);
        sqsMock.Verify(
            c => c.DeleteMessageAsync(queueUrl, "rh-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_V2MessageWithoutSourceReference_UsesMessageIdFallback()
    {
        var analysisProcessId = Guid.NewGuid();
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/analysis-completed";
        var message = new Message
        {
            MessageId = "msg-fallback",
            ReceiptHandle = "rh-1",
            Body = $$"""{"EventType":"AnalysisProcessingCompletedV2","AnalysisProcessId":"{{analysisProcessId}}","RawAiOutput":"{\"components\":\"[API]\"}","CorrelationId":"corr-1"}"""
        };

        var generationServiceMock = new Mock<IAnalysisReportGenerationService>();
        generationServiceMock
            .Setup(s => s.GenerateAsync(analysisProcessId, It.IsAny<string>(), "msg-fallback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport.GetOrGenerateAnalysisReportResponse(
                Guid.NewGuid(), analysisProcessId, "Generated", "components", "risks", "recs", "msg-fallback", 1, null, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow));

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IAnalysisReportGenerationService)))
            .Returns(generationServiceMock.Object);
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
        scope.Setup(s => s.Dispose());

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var sqsMock = new Mock<IAmazonSQS>();
        sqsMock
            .Setup(c => c.DeleteMessageAsync(queueUrl, "rh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var processor = new AnalysisCompletedMessageProcessor(
            scopeFactory.Object,
            sqsMock.Object,
            NullLogger<AnalysisCompletedMessageProcessor>.Instance);

        await processor.ProcessAsync(message, queueUrl, default);

        generationServiceMock.Verify(
            s => s.GenerateAsync(analysisProcessId, It.IsAny<string>(), "msg-fallback", It.IsAny<CancellationToken>()),
            Times.Once);
        sqsMock.Verify(
            c => c.DeleteMessageAsync(queueUrl, "rh-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_InvalidMessage_DeletesMessageWithoutGeneration()
    {
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/analysis-completed";
        var message = new Message
        {
            MessageId = "msg-2",
            ReceiptHandle = "rh-2",
            Body = "not-json"
        };

        var generationServiceMock = new Mock<IAnalysisReportGenerationService>(MockBehavior.Strict);

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IAnalysisReportGenerationService)))
            .Returns(generationServiceMock.Object);
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var sqsMock = new Mock<IAmazonSQS>();
        sqsMock
            .Setup(c => c.DeleteMessageAsync(queueUrl, "rh-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var processor = new AnalysisCompletedMessageProcessor(
            scopeFactory.Object,
            sqsMock.Object,
            NullLogger<AnalysisCompletedMessageProcessor>.Instance);

        await processor.ProcessAsync(message, queueUrl, default);

        generationServiceMock.VerifyNoOtherCalls();
        sqsMock.Verify(
            c => c.DeleteMessageAsync(queueUrl, "rh-2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_V2MessageWithoutRawAiOutput_DeletesMessageWithWarning()
    {
        var analysisProcessId = Guid.NewGuid();
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/analysis-completed";
        var message = new Message
        {
            MessageId = "msg-v2-incomplete",
            ReceiptHandle = "rh-v2-incomplete",
            Body = $$"""{"EventType":"AnalysisProcessingCompletedV2","AnalysisProcessId":"{{analysisProcessId}}","SourceAnalysisReference":"job-123","CorrelationId":"corr-1"}"""
            // Nota: sem RawAiOutput
        };

        var generationServiceMock = new Mock<IAnalysisReportGenerationService>(MockBehavior.Strict);

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(IAnalysisReportGenerationService)))
            .Returns(generationServiceMock.Object);
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var sqsMock = new Mock<IAmazonSQS>();
        sqsMock
            .Setup(c => c.DeleteMessageAsync(queueUrl, "rh-v2-incomplete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var processor = new AnalysisCompletedMessageProcessor(
            scopeFactory.Object,
            sqsMock.Object,
            NullLogger<AnalysisCompletedMessageProcessor>.Instance);

        await processor.ProcessAsync(message, queueUrl, default);

        // GenerationService não deve ser chamado
        generationServiceMock.VerifyNoOtherCalls();
        // Mas a mensagem deve ser deletada (ignorada)
        sqsMock.Verify(
            c => c.DeleteMessageAsync(queueUrl, "rh-v2-incomplete", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}