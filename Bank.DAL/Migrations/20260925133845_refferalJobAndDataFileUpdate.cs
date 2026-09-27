using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class refferalJobAndDataFileUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extra_data_json",
                table: "referral_plates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referral_columns_json",
                table: "referral_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_historical_columns_json",
                table: "referral_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "historical_plates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recorder_name",
                table: "historical_plates",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extra_data_json",
                table: "referral_plates");

            migrationBuilder.DropColumn(
                name: "referral_columns_json",
                table: "referral_jobs");

            migrationBuilder.DropColumn(
                name: "selected_historical_columns_json",
                table: "referral_jobs");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "historical_plates");

            migrationBuilder.DropColumn(
                name: "recorder_name",
                table: "historical_plates");
        }
    }
}
