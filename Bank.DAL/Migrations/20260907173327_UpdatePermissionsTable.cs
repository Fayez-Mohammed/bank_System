using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePermissionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_permissions_groups_group_id",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "can_append_to_team_files",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "can_filter",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "can_manage_admin_pins",
                table: "user_permissions");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "user_permissions",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "can_toggle_files",
                table: "user_permissions",
                newName: "can_upload_data_files");

            migrationBuilder.RenameIndex(
                name: "IX_user_permissions_group_id",
                table: "user_permissions",
                newName: "IX_user_permissions_GroupId");

            migrationBuilder.AlterColumn<bool>(
                name: "can_share",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "can_download",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AddForeignKey(
                name: "FK_user_permissions_groups_GroupId",
                table: "user_permissions",
                column: "GroupId",
                principalTable: "groups",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_permissions_groups_GroupId",
                table: "user_permissions");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "user_permissions",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "can_upload_data_files",
                table: "user_permissions",
                newName: "can_toggle_files");

            migrationBuilder.RenameIndex(
                name: "IX_user_permissions_GroupId",
                table: "user_permissions",
                newName: "IX_user_permissions_group_id");

            migrationBuilder.AlterColumn<bool>(
                name: "can_share",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "can_download",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_append_to_team_files",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_filter",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_admin_pins",
                table: "user_permissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_user_permissions_groups_group_id",
                table: "user_permissions",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
