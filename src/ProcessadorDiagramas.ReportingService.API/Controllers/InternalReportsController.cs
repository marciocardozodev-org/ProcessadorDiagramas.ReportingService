using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;
using ProcessadorDiagramas.ReportingService.Infrastructure.Data;

namespace ProcessadorDiagramas.ReportingService.API.Controllers;

[ApiController]
[Route("reports")]
[Produces("application/json")]
[Authorize(Policy = "internal")]
public sealed class InternalReportsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAmazonS3 _s3Client;
    private readonly InternalApiOptions _internalApiOptions;

    public InternalReportsController(
        AppDbContext dbContext,
        IAmazonS3 s3Client,
        IOptions<InternalApiOptions> internalApiOptions)
    {
        _dbContext = dbContext;
        _s3Client = s3Client;
        _internalApiOptions = internalApiOptions.Value;
    }

    [HttpGet("{requestId}")]
    [ProducesResponseType(typeof(InternalReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetByRequestId(
        string requestId,
        [FromQuery] bool includeContent = false,
        CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);

        if (report is null)
            return NotFound(new { message = $"Report '{requestId}' not found." });

        string? presignedUrl = null;
        string? rawContent = null;

        if (includeContent)
        {
            var objectResponse = await _s3Client.GetObjectAsync(
                report.S3ArtifactBucket,
                report.S3ArtifactKey,
                cancellationToken);

            using var reader = new StreamReader(objectResponse.ResponseStream);
            rawContent = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            var ttl = Math.Max(60, _internalApiOptions.PresignedUrlTtlSeconds);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = report.S3ArtifactBucket,
                Key = report.S3ArtifactKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddSeconds(ttl)
            };

            presignedUrl = await _s3Client.GetPreSignedURLAsync(request);
        }

        var response = new InternalReportResponse(
            report.RequestId,
            report.CorrelationId,
            report.Status,
            report.S3ArtifactBucket,
            report.S3ArtifactKey,
            report.ContentType,
            report.ContentLength,
            report.ETag,
            report.UpdatedAt,
            presignedUrl,
            rawContent);

        return Ok(response);
    }

    public sealed record InternalReportResponse(
        string RequestId,
        string CorrelationId,
        string Status,
        string S3ArtifactBucket,
        string S3ArtifactKey,
        string? ContentType,
        long? ContentLength,
        string? ETag,
        DateTime LastUpdatedAt,
        string? PresignedUrl,
        string? RawContent);
}
