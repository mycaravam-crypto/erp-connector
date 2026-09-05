using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddImportDefinitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ImportDefinition",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                RootTable = table.Column<string>(type: "TEXT", nullable: false),
                RootMatchColumn = table.Column<string>(type: "TEXT", nullable: false),
                RootNode = table.Column<string>(type: "TEXT", nullable: false),
                AllowedWritableColumns = table.Column<string>(type: "TEXT", nullable: false),
                UnmatchedRootPolicy = table.Column<string>(type: "TEXT", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                ConfigVersion = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportDefinition", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "ImportRun",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                ImportDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                ConfigVersion = table.Column<int>(type: "INTEGER", nullable: false),
                SourceFileName = table.Column<string>(type: "TEXT", nullable: false),
                Sha256Checksum = table.Column<string>(type: "TEXT", nullable: false),
                StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                FinishedAt = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                AcceptedCount = table.Column<int>(type: "INTEGER", nullable: false),
                RejectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                DiffJson = table.Column<string>(type: "TEXT", nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                OperatedBy = table.Column<string>(type: "TEXT", nullable: true),
                ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                ReleasedAt = table.Column<string>(type: "TEXT", nullable: true),
                TriggeredBy = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportRun", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_ImportRun_ImportDefinitionId",
            table: "ImportRun",
            column: "ImportDefinitionId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ImportRun");
        migrationBuilder.DropTable(name: "ImportDefinition");
    }
}
