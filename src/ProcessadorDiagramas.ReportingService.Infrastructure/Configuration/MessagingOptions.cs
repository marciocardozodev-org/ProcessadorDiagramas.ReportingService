namespace ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;

public sealed class MessagingOptions
{
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 3;
    public int WaitTimeSeconds { get; set; } = 10;
    public int MaxNumberOfMessages { get; set; } = 10;
}
