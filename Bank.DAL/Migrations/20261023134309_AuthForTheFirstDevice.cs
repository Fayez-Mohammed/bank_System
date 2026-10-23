using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AuthForTheFirstDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bound_device_hash",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bound_device_name",
                table: "users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "device_bound_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bound_device_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bound_device_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "device_bound_at",
                table: "users");
        }
    }
}
