using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class ChangesInReferralJobAndCheckFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "column_mapping",
                table: "referral_jobs");

            migrationBuilder.DropColumn(
                name: "location",
                table: "match_results");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "check_files");

            migrationBuilder.AlterColumn<string>(
                name: "found_recorded_at",
                table: "match_results",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "check_plates",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "referral_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    referral_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raw_plate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referral_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_referral_plates_referral_jobs_referral_job_id",
                        column: x => x.referral_job_id,
                        principalTable: "referral_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_referral_plates_referral_job_id_normalized_plate",
                table: "referral_plates",
                columns: new[] { "referral_job_id", "normalized_plate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_referral_plates_referral_job_id_status",
                table: "referral_plates",
                columns: new[] { "referral_job_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "referral_plates");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "check_plates");

            migrationBuilder.AddColumn<string>(
                name: "column_mapping",
                table: "referral_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "found_recorded_at",
                table: "match_results",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "location",
                table: "match_results",
                type: "geography",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "check_files",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }
    }
}
