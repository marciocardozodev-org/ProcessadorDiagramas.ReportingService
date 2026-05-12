namespace ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;

public sealed class AwsOptions
{
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
}
