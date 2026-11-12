using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace YouTubeProcessing.Orchestrator.Data;

public sealed class OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
    : DbContext(options)
{
    public DbSet<AnalysisProcess> Processes => Set<AnalysisProcess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orchestrator");
        modelBuilder.Entity<AnalysisProcess>(entity =>
        {
            entity.HasKey(x => x.JobId);
            entity.HasIndex(x => x.AnalysisId).IsUnique();
            entity.Property(x => x.Stage).HasMaxLength(32);
            entity.Property(x => x.ExtractionJson).HasColumnType("jsonb");
            entity.Property(x => x.ScenariosJson)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[\"summary\",\"topics\"]'::jsonb");
            entity.Property(x => x.RowVersion).IsRowVersion();
        });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

public sealed class AnalysisProcess
{
    public Guid JobId { get; set; }
    public Guid AnalysisId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? ExtractionJson { get; set; }
    public string ScenariosJson { get; set; } = "[\"summary\",\"topics\"]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
}
