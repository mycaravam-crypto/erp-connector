using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddStagedConnectionFingerprintToImportRun : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StagedConnectionFingerprint",
            table: "ImportRun",
            type: "TEXT",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StagedConnectionFingerprint", table: "ImportRun");
    }
}
