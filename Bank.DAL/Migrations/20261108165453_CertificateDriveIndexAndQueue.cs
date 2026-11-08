using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class CertificateDriveIndexAndQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<DateTime>(
                name: "drive_modified_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "extracted_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extraction_attempts",
                table: "certificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "file_size",
                table: "certificates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_removed_from_drive",
                table: "certificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_folder_id",
                table: "certificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "removed_from_drive_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "certificate_drive_nodes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    parent_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shortcut_target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_shared_root = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    in_scope = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    needs_scan = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificate_drive_nodes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificate_sync_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    baseline_phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    baseline_page_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    baseline_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    baseline_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    baseline_nodes_listed = table.Column<long>(type: "bigint", nullable: false),
                    baseline_files_listed = table.Column<long>(type: "bigint", nullable: false),
                    baseline_files_in_scope = table.Column<long>(type: "bigint", nullable: false),
                    extract_cutoff = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    changes_page_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_changes_sync_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reconcile_watermark = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_stats_json = table.Column<string>(type: "text", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_error_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificate_sync_state", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_discovered_at",
                table: "certificates",
                column: "discovered_at");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_extraction_queue",
                table: "certificates",
                columns: new[] { "discovered_at", "next_attempt_at" },
                filter: "status = 'Pending' AND is_removed_from_drive = false");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_file_name_trgm",
                table: "certificates",
                column: "file_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_certificates_normalized_plate_trgm",
                table: "certificates",
                column: "normalized_plate")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_parent_folder_id",
                table: "certificates",
                column: "parent_folder_id");

            migrationBuilder.CreateIndex(
                name: "IX_certificate_drive_nodes_is_shared_root",
                table: "certificate_drive_nodes",
                column: "is_shared_root",
                filter: "is_shared_root = true");

            migrationBuilder.CreateIndex(
                name: "IX_certificate_drive_nodes_needs_scan",
                table: "certificate_drive_nodes",
                column: "needs_scan",
                filter: "needs_scan = true");

            migrationBuilder.CreateIndex(
                name: "IX_certificate_drive_nodes_parent_id",
                table: "certificate_drive_nodes",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_certificate_drive_nodes_shortcut_target_id",
                table: "certificate_drive_nodes",
                column: "shortcut_target_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_drive_nodes");

            migrationBuilder.DropTable(
                name: "certificate_sync_state");

            migrationBuilder.DropIndex(
                name: "IX_certificates_discovered_at",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "ix_certificates_extraction_queue",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "ix_certificates_file_name_trgm",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "ix_certificates_normalized_plate_trgm",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "IX_certificates_parent_folder_id",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "drive_modified_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "extracted_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "extraction_attempts",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "file_size",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "is_removed_from_drive",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "parent_folder_id",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "processing_started_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "removed_from_drive_at",
                table: "certificates");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
