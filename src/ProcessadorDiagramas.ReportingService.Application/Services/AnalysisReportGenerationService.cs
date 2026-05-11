using Microsoft.Extensions.Logging;
using ProcessadorDiagramas.ReportingService.Application.Interfaces;
using ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport;
using ProcessadorDiagramas.ReportingService.Domain.Entities;
using ProcessadorDiagramas.ReportingService.Domain.Interfaces;

namespace ProcessadorDiagramas.ReportingService.Application.Services;

public sealed class AnalysisReportGenerationService : IAnalysisReportGenerationService
{
    private readonly IAnalysisReportRepository _reportRepository;
    private readonly ILogger<AnalysisReportGenerationService> _logger;

    public AnalysisReportGenerationService(
        IAnalysisReportRepository reportRepository,
        ILogger<AnalysisReportGenerationService> logger)
    {
        _reportRepository = reportRepository;
        _logger = logger;
    }

    public Task<GetOrGenerateAnalysisReportResponse?> GenerateAsync(
        Guid analysisProcessId,
        string rawAiOutput,
        string sourceAnalysisReference,
        CancellationToken cancellationToken = default)
        => UpsertInternalAsync(
            analysisProcessId,
            rawAiOutput,
            sourceAnalysisReference,
            bumpVersion: false,
            cancellationToken);

    public Task<GetOrGenerateAnalysisReportResponse?> RegenerateAsync(
        Guid analysisProcessId,
        string rawAiOutput,
        string sourceAnalysisReference,
        CancellationToken cancellationToken = default)
        => UpsertInternalAsync(
            analysisProcessId,
            rawAiOutput,
            sourceAnalysisReference,
            bumpVersion: true,
            cancellationToken);

    private async Task<GetOrGenerateAnalysisReportResponse?> UpsertInternalAsync(
        Guid analysisProcessId,
        string rawAiOutput,
        string sourceAnalysisReference,
        bool bumpVersion,
        CancellationToken cancellationToken)
    {
        if (analysisProcessId == Guid.Empty)
            throw new ArgumentException("Analysis process id cannot be empty.", nameof(analysisProcessId));

        if (string.IsNullOrWhiteSpace(rawAiOutput))
            throw new ArgumentException("Raw AI output cannot be empty.", nameof(rawAiOutput));

        if (string.IsNullOrWhiteSpace(sourceAnalysisReference))
            throw new ArgumentException("Source analysis reference cannot be empty.", nameof(sourceAnalysisReference));

        var existing = await _reportRepository.GetByAnalysisProcessIdAsync(analysisProcessId, cancellationToken);

        if (!bumpVersion && existing is not null &&
            existing.Status == Domain.Enums.AnalysisReportStatus.Generated &&
            string.Equals(existing.SourceAnalysisReference, sourceAnalysisReference, StringComparison.OrdinalIgnoreCase))
        {
            return MapToResponse(existing);
        }

        var (components, risks, recommendations) = AnalysisReportComposer.Compose(rawAiOutput);

        AnalysisReport report;
        if (existing is not null)
        {
            if (bumpVersion)
                existing.BumpVersion();

            existing.MarkAsGenerated(components, risks, recommendations, sourceAnalysisReference);
            await _reportRepository.UpdateAsync(existing, cancellationToken);
            report = existing;
        }
        else
        {
            report = AnalysisReport.CreatePending(analysisProcessId);
            report.MarkAsGenerated(components, risks, recommendations, sourceAnalysisReference);
            await _reportRepository.AddAsync(report, cancellationToken);
        }

        _logger.LogInformation(
            "Report generated from completed analysis event for analysis process {AnalysisProcessId}.",
            analysisProcessId);

        return MapToResponse(report);
    }

    private static GetOrGenerateAnalysisReportResponse MapToResponse(AnalysisReport report) =>
        new(report.Id, report.AnalysisProcessId, report.Status.ToString(),
            report.ComponentsSummary, report.ArchitecturalRisks, report.Recommendations,
            report.SourceAnalysisReference, report.Version, report.FailureReason,
            report.GeneratedAt, report.CreatedAt, report.UpdatedAt);
}