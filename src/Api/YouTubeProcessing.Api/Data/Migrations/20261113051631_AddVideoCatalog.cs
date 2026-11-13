using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YouTubeProcessing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Channels",
                schema: "api",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Videos",
                schema: "api",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LatestAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    WebpageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    ViewCount = table.Column<long>(type: "bigint", nullable: true),
                    LikeCount = table.Column<long>(type: "bigint", nullable: true),
                    FirstProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Videos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Videos_Analyses_LatestAnalysisId",
                        column: x => x.LatestAnalysisId,
                        principalSchema: "api",
                        principalTable: "Analyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Videos_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "api",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO api."Channels" ("Id", "Name", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT ON ("ChannelId")
                    "ChannelId",
                    "ChannelName",
                    MIN("CompletedAt") OVER (PARTITION BY "ChannelId"),
                    MAX("CompletedAt") OVER (PARTITION BY "ChannelId")
                FROM api."Analyses"
                WHERE "CompletedAt" IS NOT NULL
                  AND "ChannelId" IS NOT NULL
                  AND btrim("ChannelId") <> ''
                ORDER BY "ChannelId", "CompletedAt" DESC;

                INSERT INTO api."Videos" (
                    "Id",
                    "ChannelId",
                    "LatestAnalysisId",
                    "Title",
                    "Description",
                    "WebpageUrl",
                    "PublishedAt",
                    "DurationSeconds",
                    "ViewCount",
                    "LikeCount",
                    "FirstProcessedAt",
                    "LastProcessedAt")
                SELECT DISTINCT ON ("VideoId")
                    "VideoId",
                    CASE
                        WHEN "ChannelId" IS NULL OR btrim("ChannelId") = '' THEN NULL
                        ELSE "ChannelId"
                    END,
                    "Id",
                    COALESCE("Title", 'Untitled YouTube video'),
                    "VideoDescription",
                    COALESCE(
                        "WebpageUrl",
                        'https://www.youtube.com/watch?v=' || "VideoId"),
                    "PublishedAt",
                    "DurationSeconds",
                    "ViewCount",
                    "LikeCount",
                    MIN("CompletedAt") OVER (PARTITION BY "VideoId"),
                    MAX("CompletedAt") OVER (PARTITION BY "VideoId")
                FROM api."Analyses"
                WHERE "CompletedAt" IS NOT NULL
                ORDER BY "VideoId", "CompletedAt" DESC;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_UpdatedAt",
                schema: "api",
                table: "Channels",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_ChannelId",
                schema: "api",
                table: "Videos",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_LastProcessedAt",
                schema: "api",
                table: "Videos",
                column: "LastProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_LatestAnalysisId",
                schema: "api",
                table: "Videos",
                column: "LatestAnalysisId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Videos",
                schema: "api");

            migrationBuilder.DropTable(
                name: "Channels",
                schema: "api");
        }
    }
}
