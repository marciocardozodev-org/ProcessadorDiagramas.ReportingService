using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProcessadorDiagramas.ReportingService.API.Auth;
using ProcessadorDiagramas.ReportingService.Application;
using ProcessadorDiagramas.ReportingService.Infrastructure;
using ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;
using ProcessadorDiagramas.ReportingService.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var hcBuilder = builder.Services.AddHealthChecks();
if (!string.Equals(builder.Configuration["DatabaseProvider"], "InMemory", StringComparison.OrdinalIgnoreCase))
    hcBuilder.AddDbContextCheck<AppDbContext>("database");

builder.Services.AddProblemDetails();
builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection("InternalApi"));
builder.Services
    .AddAuthentication(InternalApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, InternalApiKeyAuthenticationHandler>(
        InternalApiKeyAuthenticationHandler.SchemeName,
        _ =>
        {
        });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("internal", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddAuthenticationSchemes(InternalApiKeyAuthenticationHandler.SchemeName);
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ProcessadorDiagramas.ReportingService",
        Version = "v1",
        Description = "Serviço responsável por compor e persistir relatórios técnicos estruturados a partir dos dados de análise de diagramas."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "ProcessadorDiagramas.ReportingService",
    role = "reporting-api",
    environment = app.Environment.EnvironmentName
}));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name == "database"
});

app.MapControllers();

app.Run();

public partial class Program;
