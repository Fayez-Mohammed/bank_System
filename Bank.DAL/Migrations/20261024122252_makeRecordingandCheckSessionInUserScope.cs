using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class makeRecordingandCheckSessionInUserScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RecordingSessions_DisplayName_PerGroup",
                table: "recording_sessions");

            migrationBuilder.DropIndex(
                name: "UX_RecordingSessions_DisplayName_PerSoloUser",
                table: "recording_sessions");

            migrationBuilder.DropIndex(
                name: "UX_CheckSessions_DisplayName_PerGroup",
                table: "check_sessions");

            migrationBuilder.DropIndex(
                name: "UX_CheckSessions_DisplayName_PerSoloUser",
                table: "check_sessions");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerUser",
                table: "recording_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerUser",
                table: "check_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RecordingSessions_DisplayName_PerUser",
                table: "recording_sessions");

            migrationBuilder.DropIndex(
                name: "UX_CheckSessions_DisplayName_PerUser",
                table: "check_sessions");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerGroup",
                table: "recording_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerSoloUser",
                table: "recording_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerGroup",
                table: "check_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerSoloUser",
                table: "check_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "group_id IS NULL");
        }
    }
}
