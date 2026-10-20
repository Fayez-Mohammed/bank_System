using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class user_permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // الصلاحيات الجديدة: CanManageCheckFiles / CanUploadDataFiles / CanSort.
            // can_upload_check_file بتتحول لـ can_manage_check_files عشان نحافظ على القيم الحالية،
            // وcan_download/can_share اتشالوا (التنزيل بقى جزء من CanUploadDataFiles على الملفات المملوكة).
            migrationBuilder.RenameColumn(
                name: "can_upload_check_file",
                table: "user_permissions",
                newName: "can_manage_check_files");

            migrationBuilder.DropColumn(
                name: "can_download",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "can_share",
                table: "user_permissions");

            migrationBuilder.AddColumn<bool>(
                name: "can_sort",
                table: "user_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "user_data_file_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_data_file_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_data_file_permissions_data_files_data_file_id",
                        column: x => x.data_file_id,
                        principalTable: "data_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_data_file_permissions_users_granted_by_admin_id",
                        column: x => x.granted_by_admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_data_file_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_data_file_permissions_data_file_id",
                table: "user_data_file_permissions",
                column: "data_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_data_file_permissions_granted_by_admin_id",
                table: "user_data_file_permissions",
                column: "granted_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_data_file_permissions_user_id_data_file_id",
                table: "user_data_file_permissions",
                columns: new[] { "user_id", "data_file_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_data_file_permissions");

            migrationBuilder.DropColumn(
                name: "can_sort",
                table: "user_permissions");

            migrationBuilder.RenameColumn(
                name: "can_manage_check_files",
                table: "user_permissions",
                newName: "can_upload_check_file");

            migrationBuilder.AddColumn<bool>(
                name: "can_download",
                table: "user_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_share",
                table: "user_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
