using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class checkFileUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LiveCheckLogs_GroupId_CheckedAt_MatchedOnly",
                table: "live_check_logs");

            migrationBuilder.DropIndex(
                name: "IX_LiveCheckLogs_UserId_CheckedAt_MatchedOnly",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
     name: "location",
     table: "live_check_logs");

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "live_check_logs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "check_session_id",
                table: "live_check_logs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "vehicle_type",
                table: "live_check_logs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "check_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    check_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    district = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    street = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    excel_sheet_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    total_plates = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Recording"),
                    processing_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_check_sessions_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_check_sessions_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_check_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_check_session_id_checked_at",
                table: "live_check_logs",
                columns: new[] { "check_session_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_group_id",
                table: "live_check_logs",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_is_matched",
                table: "live_check_logs",
                column: "is_matched");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_user_id_checked_at",
                table: "live_check_logs",
                columns: new[] { "user_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_check_file_id",
                table: "check_sessions",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_group_id_started_at",
                table: "check_sessions",
                columns: new[] { "group_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_user_id_started_at",
                table: "check_sessions",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerGroup",
                table: "check_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "[group_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerSoloUser",
                table: "check_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "[group_id] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_live_check_logs_check_sessions_check_session_id",
                table: "live_check_logs",
                column: "check_session_id",
                principalTable: "check_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_live_check_logs_check_sessions_check_session_id",
                table: "live_check_logs");

            migrationBuilder.DropTable(
                name: "check_sessions");

            migrationBuilder.DropIndex(
                name: "IX_live_check_logs_check_session_id_checked_at",
                table: "live_check_logs");

            migrationBuilder.DropIndex(
                name: "IX_live_check_logs_group_id",
                table: "live_check_logs");

            migrationBuilder.DropIndex(
                name: "IX_live_check_logs_is_matched",
                table: "live_check_logs");

            migrationBuilder.DropIndex(
                name: "IX_live_check_logs_user_id_checked_at",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "check_session_id",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "vehicle_type",
                table: "live_check_logs");

            migrationBuilder.AlterColumn<Point>(
                name: "location",
                table: "live_check_logs",
                type: "geography",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveCheckLogs_GroupId_CheckedAt_MatchedOnly",
                table: "live_check_logs",
                columns: new[] { "group_id", "checked_at" },
                filter: "[is_matched] = 1 AND [group_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LiveCheckLogs_UserId_CheckedAt_MatchedOnly",
                table: "live_check_logs",
                columns: new[] { "user_id", "checked_at" },
                filter: "[is_matched] = 1 AND [group_id] IS NULL");
        }
    }
}
