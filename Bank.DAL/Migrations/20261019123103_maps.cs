using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class maps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "map_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referral_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    car_count = table.Column<int>(type: "integer", nullable: false),
                    unresolved_count = table.Column<int>(type: "integer", nullable: false),
                    prepared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "map_uploaded_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_uploaded_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "map_uploaded_plate_rows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    map_uploaded_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorder_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    recorded_at = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    location_link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    extra_data_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_map_uploaded_plate_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_map_uploaded_plate_rows_map_uploaded_files_map_uploaded_fil~",
                        column: x => x.map_uploaded_file_id,
                        principalTable: "map_uploaded_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_map_results_group_id",
                table: "map_results",
                column: "group_id",
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_map_results_user_id",
                table: "map_results",
                column: "user_id",
                unique: true,
                filter: "group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_map_uploaded_files_group_id",
                table: "map_uploaded_files",
                column: "group_id",
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_map_uploaded_files_uploaded_by",
                table: "map_uploaded_files",
                column: "uploaded_by",
                unique: true,
                filter: "group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_map_uploaded_plate_rows_map_uploaded_file_id",
                table: "map_uploaded_plate_rows",
                column: "map_uploaded_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_map_uploaded_plate_rows_normalized_plate",
                table: "map_uploaded_plate_rows",
                column: "normalized_plate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "map_results");

            migrationBuilder.DropTable(
                name: "map_uploaded_plate_rows");

            migrationBuilder.DropTable(
                name: "map_uploaded_files");
        }
    }
}
