using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AmendImportRunForDesignReview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AcceptedCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "DiffJson", table: "ImportRun");

        migrationBuilder.AddColumn<int>(
            name: "ChangedCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "ConflictCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<string>(
            name: "DefinitionSnapshotJson",
            table: "ImportRun",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "InvalidCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "MatchedCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<string>(name: "PlanJson", table: "ImportRun", type: "TEXT", nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "UnchangedCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.CreateIndex(
            name: "IX_ImportRun_ImportDefinitionId_Sha256Checksum",
            table: "ImportRun",
            columns: new[] { "ImportDefinitionId", "Sha256Checksum" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_ImportRun_ImportDefinitionId_Sha256Checksum", table: "ImportRun");

        migrationBuilder.DropColumn(name: "InvalidCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "ConflictCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "UnchangedCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "ChangedCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "MatchedCount", table: "ImportRun");

        migrationBuilder.DropColumn(name: "PlanJson", table: "ImportRun");

        migrationBuilder.DropColumn(name: "DefinitionSnapshotJson", table: "ImportRun");

        migrationBuilder.AddColumn<string>(name: "DiffJson", table: "ImportRun", type: "TEXT", nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AcceptedCount",
            table: "ImportRun",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0
        );
    }
}
