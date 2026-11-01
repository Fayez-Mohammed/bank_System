using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class UserSubscriptionAndFcm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "expiry_reminder_stage",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fcm_token",
                table: "users",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fcm_token_updated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "subscription_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_fcm_token",
                table: "users",
                column: "fcm_token",
                unique: true,
                filter: "fcm_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_subscription_expires_at",
                table: "users",
                column: "subscription_expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_fcm_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_subscription_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "expiry_reminder_stage",
                table: "users");

            migrationBuilder.DropColumn(
                name: "fcm_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "fcm_token_updated_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "subscription_expires_at",
                table: "users");
        }
    }
}
