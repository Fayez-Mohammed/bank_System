using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class mapsUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_certificates_normalized_plate",
                table: "certificates");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "certificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "available_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_in_drive_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "drive_modified_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extraction_duration_ms",
                table: "certificates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted_in_drive",
                table: "certificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "certificates",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "New");

            migrationBuilder.AddColumn<string>(
                name: "parent_folder_id",
                table: "certificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_folder_name",
                table: "certificates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at",
                table: "certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "certificates",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "certificate_sync_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    baseline_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "NotStarted"),
                    baseline_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    baseline_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    baseline_page_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    baseline_pages_done = table.Column<int>(type: "integer", nullable: false),
                    baseline_files_seen = table.Column<int>(type: "integer", nullable: false),
                    baseline_indexed_historical = table.Column<int>(type: "integer", nullable: false),
                    baseline_queued_new = table.Column<int>(type: "integer", nullable: false),
                    changes_page_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    scan_watermark = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sync_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_sync_files_listed = table.Column<int>(type: "integer", nullable: false),
                    last_sync_already_indexed = table.Column<int>(type: "integer", nullable: false),
                    last_sync_indexed_historical = table.Column<int>(type: "integer", nullable: false),
                    last_sync_new_files = table.Column<int>(type: "integer", nullable: false),
                    last_sync_removed = table.Column<int>(type: "integer", nullable: false),
                    last_sync_drive_requests = table.Column<int>(type: "integer", nullable: false),
                    last_sync_duration_ms = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_reconcile_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_reconcile_marked_deleted = table.Column<int>(type: "integer", nullable: false),
                    total_drive_requests = table.Column<long>(type: "bigint", nullable: false),
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
                name: "IX_certificates_file_name_trgm",
                table: "certificates",
                column: "file_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_normalized_plate_trgm",
                table: "certificates",
                column: "normalized_plate")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_parent_folder_id",
                table: "certificates",
                column: "parent_folder_id");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_pending_queue",
                table: "certificates",
                columns: new[] { "batch_no", "drive_created_at" },
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_sync_state");

            migrationBuilder.DropIndex(
                name: "IX_certificates_discovered_at",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "IX_certificates_file_name_trgm",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "IX_certificates_normalized_plate_trgm",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "IX_certificates_parent_folder_id",
                table: "certificates");

            migrationBuilder.DropIndex(
                name: "IX_certificates_pending_queue",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "available_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "deleted_in_drive_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "drive_modified_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "extraction_duration_ms",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "is_deleted_in_drive",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "parent_folder_id",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "parent_folder_name",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "processed_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "processing_started_at",
                table: "certificates");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "certificates");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_normalized_plate",
                table: "certificates",
                column: "normalized_plate");
        }
    }
}
