using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProcessadorDiagramas.ReportingService.Application.Services;
using ProcessadorDiagramas.ReportingService.Domain.Entities;
using ProcessadorDiagramas.ReportingService.Domain.Interfaces;

namespace ProcessadorDiagramas.ReportingService.Tests.Application;

public sealed class AnalysisReportGenerationServiceTests
{
    private readonly Mock<IAnalysisReportRepository> _repositoryMock = new();
    private readonly AnalysisReportGenerationService _service;

    public AnalysisReportGenerationServiceTests()
    {
        _service = new AnalysisReportGenerationService(
            _repositoryMock.Object,
            NullLogger<AnalysisReportGenerationService>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_CreatesReportFromRawOutput()
    {
        var analysisProcessId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString();
        var rawOutput = """{"components":"[API]","risks":"[SPOF]","recommendations":"[add retry]"}""";

        _repositoryMock
            .Setup(r => r.GetByAnalysisProcessIdAsync(analysisProcessId, default))
            .ReturnsAsync((AnalysisReport?)null);

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<AnalysisReport>(), default))
            .Returns(Task.CompletedTask);

        var result = await _service.GenerateAsync(
            analysisProcessId,
            rawOutput,
            sourceReference);

        result.Should().NotBeNull();
        result!.Status.Should().Be("Generated");
        result.AnalysisProcessId.Should().Be(analysisProcessId);
        result.SourceAnalysisReference.Should().Be(sourceReference);
        result.ComponentsSummary.Should().Contain("API");

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<AnalysisReport>(), default), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AnalysisReport>(), default), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsCachedReportWhenSourceAlreadyGenerated()
    {
        var analysisProcessId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString();
        var report = AnalysisReport.CreatePending(analysisProcessId);
        report.MarkAsGenerated("comps", "risks", "recs", sourceReference);

        _repositoryMock
            .Setup(r => r.GetByAnalysisProcessIdAsync(analysisProcessId, default))
            .ReturnsAsync(report);

        var result = await _service.GenerateAsync(
            analysisProcessId,
            "{\"components\":\"ignored\"}",
            sourceReference);

        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
        result.SourceAnalysisReference.Should().Be(sourceReference);

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<AnalysisReport>(), default), Times.Never);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AnalysisReport>(), default), Times.Never);
    }

    [Fact]
    public async Task RegenerateAsync_BumpsVersionWhenReportExists()
    {
        var analysisProcessId = Guid.NewGuid();
        var sourceReference = Guid.NewGuid().ToString();
        var report = AnalysisReport.CreatePending(analysisProcessId);
        report.MarkAsGenerated("old-comps", "old-risks", "old-recs", "old-ref");

        _repositoryMock
            .Setup(r => r.GetByAnalysisProcessIdAsync(analysisProcessId, default))
            .ReturnsAsync(report);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AnalysisReport>(), default))
            .Returns(Task.CompletedTask);

        var result = await _service.RegenerateAsync(
            analysisProcessId,
            """{"components":"[DB]","risks":"[backup]","recommendations":"[replica]"}""",
            sourceReference);

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
        result.SourceAnalysisReference.Should().Be(sourceReference);
        result.ComponentsSummary.Should().Contain("DB");

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<AnalysisReport>(), default), Times.Once);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<AnalysisReport>(), default), Times.Never);
    }
}