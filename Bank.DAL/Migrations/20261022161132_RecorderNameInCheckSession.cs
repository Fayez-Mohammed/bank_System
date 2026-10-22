using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RecorderNameInCheckSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recorder_name",
                table: "check_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recorder_name",
                table: "check_sessions");
        }
    }
}
