using Microsoft.EntityFrameworkCore;
using ProcessadorDiagramas.ReportingService.Domain.Entities;

namespace ProcessadorDiagramas.ReportingService.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AnalysisReport> AnalysisReports => Set<AnalysisReport>();
    public DbSet<ReportRecord> Reports => Set<ReportRecord>();
    public DbSet<ProcessedInboxMessage> ProcessedInboxMessages => Set<ProcessedInboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisReport>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(r => r.ComponentsSummary).HasColumnType("text");
            entity.Property(r => r.ArchitecturalRisks).HasColumnType("text");
            entity.Property(r => r.Recommendations).HasColumnType("text");
            entity.Property(r => r.SourceAnalysisReference).HasMaxLength(500);
            entity.Property(r => r.FailureReason).HasMaxLength(2000);
            entity.HasIndex(r => r.AnalysisProcessId).IsUnique();
        });

        modelBuilder.Entity<ReportRecord>(entity =>
        {
            entity.ToTable("reports");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnName("id");
            entity.Property(r => r.RequestId).HasColumnName("requestid").HasMaxLength(100).IsRequired();
            entity.Property(r => r.CorrelationId).HasColumnName("correlationid").HasMaxLength(100).IsRequired();
            entity.Property(r => r.S3ArtifactBucket).HasColumnName("s3artifactbucket").HasMaxLength(255).IsRequired();
            entity.Property(r => r.S3ArtifactKey).HasColumnName("s3artifactkey").HasMaxLength(1024).IsRequired();
            entity.Property(r => r.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(r => r.ETag).HasColumnName("etag").HasMaxLength(200);
            entity.Property(r => r.ContentType).HasColumnName("contenttype").HasMaxLength(200);
            entity.Property(r => r.ContentLength).HasColumnName("contentlength");
            entity.Property(r => r.CreatedAt).HasColumnName("createdat");
            entity.Property(r => r.UpdatedAt).HasColumnName("updatedat");
            entity.HasIndex(r => r.RequestId).IsUnique();
        });

        modelBuilder.Entity<ProcessedInboxMessage>(entity =>
        {
            entity.ToTable("processed_inbox_messages");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.CorrelationId).HasColumnName("correlationid").HasMaxLength(100).IsRequired();
            entity.Property(i => i.RequestId).HasColumnName("requestid").HasMaxLength(100).IsRequired();
            entity.Property(i => i.SourceQueue).HasColumnName("sourcequeue").HasMaxLength(200).IsRequired();
            entity.Property(i => i.MessageId).HasColumnName("messageid").HasMaxLength(200).IsRequired();
            entity.Property(i => i.ProcessedAt).HasColumnName("processedat");
            entity.HasIndex(i => i.CorrelationId).IsUnique();
        });
    }
}
