using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class LiveCheckLogAppendedToDataFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "appended_at",
                table: "live_check_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "appended_to_data_file_id",
                table: "live_check_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_appended_to_data_file_id",
                table: "live_check_logs",
                column: "appended_to_data_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_live_check_logs_appended_to_data_file_id",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "appended_at",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "appended_to_data_file_id",
                table: "live_check_logs");
        }
    }
}
