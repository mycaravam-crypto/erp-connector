using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddExportDefinitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExportDefinition",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                RootTable = table.Column<string>(type: "TEXT", nullable: false),
                RootNode = table.Column<string>(type: "TEXT", nullable: false),
                OutputFormat = table.Column<string>(type: "TEXT", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Schedule = table.Column<string>(type: "TEXT", nullable: true),
                ConfigVersion = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportDefinition", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "ExportDefinitionRun",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                ExportDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                ConfigVersion = table.Column<int>(type: "INTEGER", nullable: false),
                StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                FinishedAt = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                TriggeredBy = table.Column<string>(type: "TEXT", nullable: false),
                IsTestRun = table.Column<bool>(type: "INTEGER", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportDefinitionRun", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_ExportDefinitionRun_ExportDefinitionId",
            table: "ExportDefinitionRun",
            column: "ExportDefinitionId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExportDefinitionRun");
        migrationBuilder.DropTable(name: "ExportDefinition");
    }
}
