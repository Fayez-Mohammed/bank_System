using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class ManualCheckTableAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_checks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raw_plate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    check_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_matched = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    matched_check_plate_id = table.Column<long>(type: "bigint", nullable: true),
                    location = table.Column<Point>(type: "geography", nullable: true),
                    checked_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manual_checks_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manual_checks_check_plates_matched_check_plate_id",
                        column: x => x.matched_check_plate_id,
                        principalTable: "check_plates",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_manual_checks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_check_file_id",
                table: "manual_checks",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_matched_check_plate_id",
                table: "manual_checks",
                column: "matched_check_plate_id");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_normalized_plate",
                table: "manual_checks",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_user_id_checked_at",
                table: "manual_checks",
                columns: new[] { "user_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_user_id_is_matched",
                table: "manual_checks",
                columns: new[] { "user_id", "is_matched" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_checks");
        }
    }
}
