using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RecordingMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "check_file_id",
                table: "recording_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "recording_sessions",
                type: "nvarchar(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "processing_error",
                table: "recording_sessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "audio_offset_ms",
                table: "recorded_plates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence_number",
                table: "recorded_plates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "recording_gps_points",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    location = table.Column<Point>(type: "geography", nullable: false),
                    captured_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    accuracy_meters = table.Column<double>(type: "float", nullable: true),
                    is_manual = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_gps_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_recording_gps_points_recording_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "recording_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recording_sessions_check_file_id",
                table: "recording_sessions",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerGroup",
                table: "recording_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "[group_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerSoloUser",
                table: "recording_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "[group_id] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_recording_gps_points_session_id_captured_at",
                table: "recording_gps_points",
                columns: new[] { "session_id", "captured_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_recording_sessions_check_files_check_file_id",
                table: "recording_sessions",
                column: "check_file_id",
                principalTable: "check_files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recording_sessions_check_files_check_file_id",
                table: "recording_sessions");

            migrationBuilder.DropTable(
                name: "recording_gps_points");

            migrationBuilder.DropIndex(
                name: "IX_recording_sessions_check_file_id",
                table: "recording_sessions");

            migrationBuilder.DropIndex(
                name: "UX_RecordingSessions_DisplayName_PerGroup",
                table: "recording_sessions");

            migrationBuilder.DropIndex(
                name: "UX_RecordingSessions_DisplayName_PerSoloUser",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "check_file_id",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "processing_error",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "audio_offset_ms",
                table: "recorded_plates");

            migrationBuilder.DropColumn(
                name: "sequence_number",
                table: "recorded_plates");
        }
    }
}
