using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YouTubeProcessing.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScenariosJson",
                schema: "api",
                table: "Analyses",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"summary\",\"topics\"]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScenariosJson",
                schema: "api",
                table: "Analyses");
        }
    }
}
