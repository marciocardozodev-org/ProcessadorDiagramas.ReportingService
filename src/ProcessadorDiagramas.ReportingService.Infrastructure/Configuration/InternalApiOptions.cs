namespace ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;

public sealed class InternalApiOptions
{
    public string ApiKeyHeaderName { get; set; } = "x-internal-api-key";
    public string ApiKey { get; set; } = string.Empty;
    public int PresignedUrlTtlSeconds { get; set; } = 300;
}
