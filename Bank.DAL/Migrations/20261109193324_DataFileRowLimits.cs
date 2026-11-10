using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class DataFileRowLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "max_data_file_rows",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "max_data_file_rows",
                table: "groups",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_data_file_rows",
                table: "users");

            migrationBuilder.DropColumn(
                name: "max_data_file_rows",
                table: "groups");
        }
    }
}
