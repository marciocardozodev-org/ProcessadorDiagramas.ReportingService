using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ProcessadorDiagramas.ReportingService.Application.Interfaces;
using ProcessadorDiagramas.ReportingService.Infrastructure.BackgroundServices;
using ProcessadorDiagramas.ReportingService.Domain.Interfaces;
using ProcessadorDiagramas.ReportingService.Infrastructure.Clients;
using ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;
using ProcessadorDiagramas.ReportingService.Infrastructure.Data;
using ProcessadorDiagramas.ReportingService.Infrastructure.Data.Repositories;
using ProcessadorDiagramas.ReportingService.Infrastructure.Messaging;

namespace ProcessadorDiagramas.ReportingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var databaseProvider = configuration["DatabaseProvider"];
        services.AddDbContext<AppDbContext>(options =>
        {
            if (string.Equals(databaseProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
                options.UseInMemoryDatabase("ReportingServiceE2E");
            else
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddScoped<IAnalysisReportRepository, AnalysisReportRepository>();

        services.Configure<ProcessingServiceSettings>(configuration.GetSection("ProcessingService"));
        services.AddHttpClient<ProcessingServiceClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<ProcessingServiceSettings>>().Value;

            if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
                httpClient.BaseAddress = baseUri;

            if (settings.TimeoutSeconds > 0)
                httpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });
        services.AddScoped<IProcessingServiceClient>(sp => sp.GetRequiredService<ProcessingServiceClient>());

        services.Configure<AwsOptions>(configuration.GetSection("Aws"));
        services.Configure<QueuesOptions>(configuration.GetSection("Queues"));
        services.Configure<MessagingOptions>(configuration.GetSection("Messaging"));
        services.Configure<InternalApiOptions>(configuration.GetSection("InternalApi"));

        services.AddSingleton<IAmazonSQS>(serviceProvider =>
        {
            var awsOptions = serviceProvider.GetRequiredService<IOptions<AwsOptions>>().Value;
            return CreateSqsClient(awsOptions);
        });
        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var awsOptions = serviceProvider.GetRequiredService<IOptions<AwsOptions>>().Value;
            return CreateS3Client(awsOptions);
        });
        services.AddSingleton<SqsQueueUrlResolver>();
        services.AddScoped<AnalysisCompletedMessageProcessor>();

        var messagingOptions = configuration.GetSection("Messaging").Get<MessagingOptions>() ?? new MessagingOptions();
        if (messagingOptions.Enabled)
            services.AddHostedService<AnalysisCompletedConsumerBackgroundService>();

        return services;
    }

    private static IAmazonSQS CreateSqsClient(AwsOptions options)
    {
        var config = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(options.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            if (!string.IsNullOrWhiteSpace(options.Region))
                config.AuthenticationRegion = options.Region;
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            return new AmazonSQSClient(config);

        AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
            ? new BasicAWSCredentials(options.AccessKey, options.SecretKey)
            : new SessionAWSCredentials(options.AccessKey, options.SecretKey, options.SessionToken);

        return new AmazonSQSClient(credentials, config);
    }

    private static IAmazonS3 CreateS3Client(AwsOptions options)
    {
        var config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(options.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            if (!string.IsNullOrWhiteSpace(options.Region))
                config.AuthenticationRegion = options.Region;
            config.ForcePathStyle = true;
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            return new AmazonS3Client(config);

        AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
            ? new BasicAWSCredentials(options.AccessKey, options.SecretKey)
            : new SessionAWSCredentials(options.AccessKey, options.SecretKey, options.SessionToken);

        return new AmazonS3Client(credentials, config);
    }
}
