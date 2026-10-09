using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RecordingArchiveAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recording_archive_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raw_plate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    recorder_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    source_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    appended_to_data_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    appended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_archive_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_recording_archive_plates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_normalized_plate",
                table: "recording_archive_plates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_source_session_id",
                table: "recording_archive_plates",
                column: "source_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_user_id",
                table: "recording_archive_plates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_user_id_appended_to_data_file_id",
                table: "recording_archive_plates",
                columns: new[] { "user_id", "appended_to_data_file_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recording_archive_plates");
        }
    }
}
