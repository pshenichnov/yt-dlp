using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace YouTubeProcessing.Api.Data;

public sealed class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<AnalysisEntity> Analyses => Set<AnalysisEntity>();
    public DbSet<ArtifactEntity> Artifacts => Set<ArtifactEntity>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("api");

        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.Stage).HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(128);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AnalysisEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.JobId).IsUnique();
            entity.HasIndex(x => x.VideoId);
            entity.Property(x => x.VideoId).HasMaxLength(32);
            entity.Property(x => x.SubjectsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ArtifactEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AnalysisId, x.Kind }).IsUnique();
            entity.Property(x => x.Kind).HasMaxLength(32);
            entity.Property(x => x.ObjectKey).HasMaxLength(1024);
            entity.Property(x => x.ContentType).HasMaxLength(256);
            entity.Property(x => x.Sha256).HasMaxLength(64);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

public sealed class JobEntity
{
    public Guid Id { get; set; }
    public Guid AnalysisId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class AnalysisEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string VideoId { get; set; } = string.Empty;
    public string OriginalInput { get; set; } = string.Empty;
    public string? PreferredLanguage { get; set; }
    public string? Title { get; set; }
    public string? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long? DurationSeconds { get; set; }
    public long? ViewCount { get; set; }
    public long? LikeCount { get; set; }
    public string? VideoDescription { get; set; }
    public string? WebpageUrl { get; set; }
    public string? Summary { get; set; }
    public string? SubjectsJson { get; set; }
    public string? AnalysisModel { get; set; }
    public string? PromptVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ArtifactEntity
{
    public Guid Id { get; set; }
    public Guid AnalysisId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
