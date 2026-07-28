using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YouTubeProcessing.Orchestrator.Data;

public sealed class OrchestratorDbContextFactory
    : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql("Host=localhost;Database=youtube_orchestrator;Username=youtube;Password=youtube_dev")
            .Options;
        return new OrchestratorDbContext(options);
    }
}
