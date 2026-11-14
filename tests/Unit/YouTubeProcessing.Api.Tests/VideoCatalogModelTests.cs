using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using YouTubeProcessing.Api.Data;

namespace YouTubeProcessing.Api.Tests;

public sealed class VideoCatalogModelTests
{
    [Fact]
    public void VideoCatalogSupportsMetadataOnlyChannelVideos()
    {
        using var db = CreateDbContext();
        var channel = db.Model.FindEntityType(typeof(ChannelEntity));
        var video = db.Model.FindEntityType(typeof(VideoEntity));

        Assert.NotNull(channel);
        Assert.NotNull(video);
        Assert.Equal(nameof(ChannelEntity.Id), channel.FindPrimaryKey()!.Properties[0].Name);
        Assert.Equal(64, channel.FindProperty(nameof(ChannelEntity.Id))!.GetMaxLength());
        Assert.Equal(100, channel.FindProperty(nameof(ChannelEntity.Handle))!.GetMaxLength());
        Assert.True(channel.GetIndexes().Single(
            x => x.Properties.Single().Name == nameof(ChannelEntity.Handle)).IsUnique);
        Assert.Equal(nameof(VideoEntity.Id), video.FindPrimaryKey()!.Properties[0].Name);
        Assert.Equal(32, video.FindProperty(nameof(VideoEntity.Id))!.GetMaxLength());

        var channelRelationship = video.GetForeignKeys().Single(
            x => x.PrincipalEntityType.ClrType == typeof(ChannelEntity));
        Assert.False(channelRelationship.IsRequired);
        Assert.Equal(DeleteBehavior.SetNull, channelRelationship.DeleteBehavior);

        var analysisRelationship = video.GetForeignKeys().Single(
            x => x.PrincipalEntityType.ClrType == typeof(AnalysisEntity));
        Assert.False(analysisRelationship.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, analysisRelationship.DeleteBehavior);
        Assert.True(video.FindProperty(nameof(VideoEntity.LatestAnalysisId))!.IsNullable);
        Assert.True(video.FindProperty(nameof(VideoEntity.LastProcessedAt))!.IsNullable);
        Assert.Equal(2048, video.FindProperty(nameof(VideoEntity.ThumbnailUrl))!.GetMaxLength());
        Assert.Equal(
            ChannelSyncStatuses.Ready,
            channel.FindProperty(nameof(ChannelEntity.SyncStatus))!.GetDefaultValue());
    }

    private static ApiDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        return new ApiDbContext(options);
    }
}
