using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddProducerToImportRun : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Producer", table: "ImportRun", type: "TEXT", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Producer", table: "ImportRun");
    }
}
