using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YouTubeProcessing.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScenariosJson",
                schema: "orchestrator",
                table: "Processes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"summary\",\"topics\"]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScenariosJson",
                schema: "orchestrator",
                table: "Processes");
        }
    }
}
