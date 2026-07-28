using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YouTubeProcessing.Analyzer.Data;

public sealed class AnalyzerDbContextFactory
    : IDesignTimeDbContextFactory<AnalyzerDbContext>
{
    public AnalyzerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnalyzerDbContext>()
            .UseNpgsql("Host=localhost;Database=youtube_analyzer;Username=youtube;Password=youtube_dev")
            .Options;
        return new AnalyzerDbContext(options);
    }
}
