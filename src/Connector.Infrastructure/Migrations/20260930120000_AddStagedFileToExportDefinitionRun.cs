using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddStagedFileToExportDefinitionRun : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DataFileName",
            table: "ExportDefinitionRun",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(name: "Sha256", table: "ExportDefinitionRun", type: "TEXT", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DataFileName", table: "ExportDefinitionRun");

        migrationBuilder.DropColumn(name: "Sha256", table: "ExportDefinitionRun");
    }
}
