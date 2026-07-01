using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Connector.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppSetting",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppSetting", x => x.Key);
            }
        );

        migrationBuilder.CreateTable(
            name: "AuditLog",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                Action = table.Column<string>(type: "TEXT", nullable: false),
                Detail = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLog", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "ExportRun",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SequenceNo = table.Column<int>(type: "INTEGER", nullable: false),
                ExtractedAt = table.Column<string>(type: "TEXT", nullable: false),
                RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                ReleasedAt = table.Column<string>(type: "TEXT", nullable: true),
                OperatedBy = table.Column<string>(type: "TEXT", nullable: true),
                ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                DataFileName = table.Column<string>(type: "TEXT", nullable: false),
                DeliveredAt = table.Column<string>(type: "TEXT", nullable: true),
                DeliveredBy = table.Column<string>(type: "TEXT", nullable: true),
                ImportedRecordCount = table.Column<int>(type: "INTEGER", nullable: true),
                DeliveryNotes = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportRun", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_AuditLog_Timestamp",
            table: "AuditLog",
            column: "Timestamp"
        );

        migrationBuilder.CreateIndex(
            name: "IX_ExportRun_SequenceNo",
            table: "ExportRun",
            column: "SequenceNo",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AppSetting");
        migrationBuilder.DropTable(name: "AuditLog");
        migrationBuilder.DropTable(name: "ExportRun");
    }
}
