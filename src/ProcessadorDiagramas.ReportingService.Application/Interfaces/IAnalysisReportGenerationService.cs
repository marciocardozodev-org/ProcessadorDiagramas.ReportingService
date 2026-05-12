using ProcessadorDiagramas.ReportingService.Application.Queries.GetOrGenerateAnalysisReport;

namespace ProcessadorDiagramas.ReportingService.Application.Interfaces;

public interface IAnalysisReportGenerationService
{
    Task<GetOrGenerateAnalysisReportResponse?> GenerateAsync(
        Guid analysisProcessId,
        string rawAiOutput,
        string sourceAnalysisReference,
        CancellationToken cancellationToken = default);

    Task<GetOrGenerateAnalysisReportResponse?> RegenerateAsync(
        Guid analysisProcessId,
        string rawAiOutput,
        string sourceAnalysisReference,
        CancellationToken cancellationToken = default);
}