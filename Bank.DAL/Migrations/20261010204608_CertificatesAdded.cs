using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class CertificatesAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    drive_file_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    batch_no = table.Column<int>(type: "int", nullable: false),
                    discovered_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Sijil"),
                    plate_letters = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    plate_digits = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    normalized_plate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    make = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    color = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    lessor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    status_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    storage_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_manually_edited = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_batch_no",
                table: "certificates",
                column: "batch_no");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_drive_file_id",
                table: "certificates",
                column: "drive_file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_certificates_normalized_plate",
                table: "certificates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_status",
                table: "certificates",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificates");
        }
    }
}
