using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddPlateCorrectionsAndTrainingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "recording_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                table: "recording_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_recorded_plate_id",
                table: "recording_archive_plates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "audio_end_ms",
                table: "recorded_plates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "audio_start_ms",
                table: "recorded_plates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "audio_offset_ms",
                table: "live_check_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "live_check_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "check_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                table: "check_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plate_corrections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    plate_id = table.Column<long>(type: "bigint", nullable: true),
                    archive_row_id = table.Column<long>(type: "bigint", nullable: true),
                    is_manual_plate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    old_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    new_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    old_vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    new_vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    audio_offset_ms = table.Column<int>(type: "integer", nullable: true),
                    audio_start_ms = table.Column<int>(type: "integer", nullable: true),
                    audio_end_ms = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plate_corrections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plate_corrections_session_id",
                table: "plate_corrections",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plate_corrections");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "recording_sessions");

            migrationBuilder.DropColumn(
                name: "source_recorded_plate_id",
                table: "recording_archive_plates");

            migrationBuilder.DropColumn(
                name: "audio_end_ms",
                table: "recorded_plates");

            migrationBuilder.DropColumn(
                name: "audio_start_ms",
                table: "recorded_plates");

            migrationBuilder.DropColumn(
                name: "audio_offset_ms",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "check_sessions");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "check_sessions");
        }
    }
}
