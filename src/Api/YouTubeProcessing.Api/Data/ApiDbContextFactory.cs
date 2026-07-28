using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YouTubeProcessing.Api.Data;

public sealed class ApiDbContextFactory : IDesignTimeDbContextFactory<ApiDbContext>
{
    public ApiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseNpgsql("Host=localhost;Database=youtube_api;Username=youtube;Password=youtube_dev")
            .Options;
        return new ApiDbContext(options);
    }
}
