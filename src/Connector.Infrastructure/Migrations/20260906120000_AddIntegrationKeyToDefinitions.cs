using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddIntegrationKeyToDefinitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IntegrationKey",
            table: "ExportDefinition",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "ContractVersion",
            table: "ExportDefinition",
            type: "INTEGER",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "CorrelationKeySourceField",
            table: "ExportDefinition",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "IntegrationKey",
            table: "ImportDefinition",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "ContractVersion",
            table: "ImportDefinition",
            type: "INTEGER",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContractVersion", table: "ImportDefinition");

        migrationBuilder.DropColumn(name: "IntegrationKey", table: "ImportDefinition");

        migrationBuilder.DropColumn(name: "CorrelationKeySourceField", table: "ExportDefinition");

        migrationBuilder.DropColumn(name: "ContractVersion", table: "ExportDefinition");

        migrationBuilder.DropColumn(name: "IntegrationKey", table: "ExportDefinition");
    }
}
