using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace YouTubeProcessing.Analyzer.Data;

public sealed class AnalyzerDbContext(DbContextOptions<AnalyzerDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("analyzer");
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
