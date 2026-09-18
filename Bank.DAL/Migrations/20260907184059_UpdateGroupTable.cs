using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGroupTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_groups_users_admin_id",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_user_permissions_groups_GroupId",
                table: "user_permissions");

            migrationBuilder.DropIndex(
                name: "IX_user_permissions_GroupId",
                table: "user_permissions");

            migrationBuilder.DropIndex(
                name: "IX_groups_admin_id",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "user_permissions");

            migrationBuilder.AlterColumn<Guid>(
                name: "admin_id",
                table: "groups",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_groups_admin_id",
                table: "groups",
                column: "admin_id",
                unique: true,
                filter: "[admin_id] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_groups_users_admin_id",
                table: "groups",
                column: "admin_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_groups_users_admin_id",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_groups_admin_id",
                table: "groups");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "user_permissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "admin_id",
                table: "groups",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_GroupId",
                table: "user_permissions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_admin_id",
                table: "groups",
                column: "admin_id");

            migrationBuilder.AddForeignKey(
                name: "FK_groups_users_admin_id",
                table: "groups",
                column: "admin_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_permissions_groups_GroupId",
                table: "user_permissions",
                column: "GroupId",
                principalTable: "groups",
                principalColumn: "id");
        }
    }
}
