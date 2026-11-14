using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YouTubeProcessing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LatestAnalysisId",
                schema: "api",
                table: "Videos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastProcessedAt",
                schema: "api",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstProcessedAt",
                schema: "api",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiscoveredAt",
                schema: "api",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                schema: "api",
                table: "Videos",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasMoreVideos",
                schema: "api",
                table: "Channels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                schema: "api",
                table: "Channels",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextPageStartIndex",
                schema: "api",
                table: "Channels",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingRequestId",
                schema: "api",
                table: "Channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncStatus",
                schema: "api",
                table: "Channels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ready");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM api."Videos"
                WHERE "LatestAnalysisId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "DiscoveredAt",
                schema: "api",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                schema: "api",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "HasMoreVideos",
                schema: "api",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                schema: "api",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "NextPageStartIndex",
                schema: "api",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PendingRequestId",
                schema: "api",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                schema: "api",
                table: "Channels");

            migrationBuilder.AlterColumn<Guid>(
                name: "LatestAnalysisId",
                schema: "api",
                table: "Videos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastProcessedAt",
                schema: "api",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstProcessedAt",
                schema: "api",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
