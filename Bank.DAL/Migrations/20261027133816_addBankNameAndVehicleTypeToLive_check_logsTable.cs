using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class addBankNameAndVehicleTypeToLive_check_logsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "live_check_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_type_in_check_file",
                table: "live_check_logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "live_check_logs");

            migrationBuilder.DropColumn(
                name: "vehicle_type_in_check_file",
                table: "live_check_logs");
        }
    }
}
