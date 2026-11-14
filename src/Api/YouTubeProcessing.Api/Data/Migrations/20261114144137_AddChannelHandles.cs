using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YouTubeProcessing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelHandles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Handle",
                schema: "api",
                table: "Channels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Handle",
                schema: "api",
                table: "Channels",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Channels_Handle",
                schema: "api",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "Handle",
                schema: "api",
                table: "Channels");
        }
    }
}
