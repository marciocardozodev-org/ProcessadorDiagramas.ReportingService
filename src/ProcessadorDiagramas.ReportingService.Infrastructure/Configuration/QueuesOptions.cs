namespace ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;

public sealed class QueuesOptions
{
    public string AnalysisCompletedQueueName { get; set; } = "upload-orchestrator-analysis-completed";
}
