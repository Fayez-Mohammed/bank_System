using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.DAL.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    drive_file_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    batch_no = table.Column<int>(type: "integer", nullable: false),
                    discovered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Sijil"),
                    plate_letters = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    plate_digits = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lessor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    status_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_manually_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    old_values = table.Column<string>(type: "text", nullable: true),
                    new_values = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "check_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    total_plates = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "check_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    check_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    chassis_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    claim_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_check_plates_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "check_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    check_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    excel_sheet_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    total_plates = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Recording"),
                    processing_error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_check_sessions_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "data_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active_for_filtering = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    import_error = table.Column<string>(type: "text", nullable: true),
                    import_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    share_admin_pins = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    admin_pins_shared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_shared_referral_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "historical_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    data_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorder_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historical_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_historical_plates_data_files_data_file_id",
                        column: x => x.data_file_id,
                        principalTable: "data_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_historical_plates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "live_check_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    check_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    check_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Live"),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    matched_check_plate_id = table.Column<long>(type: "bigint", nullable: true),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_live_check_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_live_check_logs_check_plates_matched_check_plate_id",
                        column: x => x.matched_check_plate_id,
                        principalTable: "check_plates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_live_check_logs_check_sessions_check_session_id",
                        column: x => x.check_session_id,
                        principalTable: "check_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_live_check_logs_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_live_check_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manual_checks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    check_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    matched_check_plate_id = table.Column<long>(type: "bigint", nullable: true),
                    location = table.Column<Point>(type: "geography", nullable: true),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_manual_checks_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manual_checks_check_plates_matched_check_plate_id",
                        column: x => x.matched_check_plate_id,
                        principalTable: "check_plates",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_manual_checks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recording_archive_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorder_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    source_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    appended_to_data_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_archive_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_recording_archive_plates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recording_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_record = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    check_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    excel_sheet_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    recorder_name = table.Column<string>(type: "text", nullable: true),
                    tracking_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Auto"),
                    tracking_interval_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    total_gps_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_plates = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    audio_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    processing_error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_recording_sessions_check_files_check_file_id",
                        column: x => x.check_file_id,
                        principalTable: "check_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recording_sessions_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recording_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "referral_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    filter_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    total_referral_plates = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    skipped_plates_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    matched_plates_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Processing"),
                    selected_historical_columns_json = table.Column<string>(type: "text", nullable: true),
                    referral_columns_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referral_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_referral_jobs_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_referral_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    jwt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_logins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_upload_data_files = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    can_download = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    can_share = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    can_upload_check_file = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recorded_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    location = table.Column<Point>(type: "geography", nullable: true),
                    audio_offset_ms = table.Column<int>(type: "integer", nullable: true),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    matched_check_plate_id = table.Column<long>(type: "bigint", nullable: true),
                    is_manual_entry = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    vehicle_type_in_check_file = table.Column<string>(type: "text", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recorded_plates", x => x.id);
                    table.ForeignKey(
                        name: "FK_recorded_plates_check_plates_matched_check_plate_id",
                        column: x => x.matched_check_plate_id,
                        principalTable: "check_plates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recorded_plates_recording_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "recording_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recorded_plates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recording_gps_points",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<Point>(type: "geography", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_gps_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_recording_gps_points_recording_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "recording_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    referral_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    historical_plate_id = table.Column<long>(type: "bigint", nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    found_vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    found_district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    found_street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    found_recorded_at = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_data_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_results_data_files_source_data_file_id",
                        column: x => x.source_data_file_id,
                        principalTable: "data_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_match_results_historical_plates_historical_plate_id",
                        column: x => x.historical_plate_id,
                        principalTable: "historical_plates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_match_results_referral_jobs_referral_job_id",
                        column: x => x.referral_job_id,
                        principalTable: "referral_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "referral_plates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    referral_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extra_data_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
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
                name: "IX_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_group_id_created_at",
                table: "audit_logs",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id_created_at",
                table: "audit_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_certificates_batch_no",
                table: "certificates",
                column: "batch_no");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_drive_file_id",
                table: "certificates",
                column: "drive_file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_certificates_normalized_plate",
                table: "certificates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_certificates_status",
                table: "certificates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "UX_CheckFiles_OneActivePerGroup",
                table: "check_files",
                column: "group_id",
                unique: true,
                filter: "is_active = true AND group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckFiles_OneActivePerSoloUser",
                table: "check_files",
                column: "uploaded_by",
                unique: true,
                filter: "is_active = true AND group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_check_plates_check_file_id_normalized_plate",
                table: "check_plates",
                columns: new[] { "check_file_id", "normalized_plate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_check_plates_normalized_plate",
                table: "check_plates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_check_file_id",
                table: "check_sessions",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_group_id_started_at",
                table: "check_sessions",
                columns: new[] { "group_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_check_sessions_user_id_started_at",
                table: "check_sessions",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerGroup",
                table: "check_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckSessions_DisplayName_PerSoloUser",
                table: "check_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_data_files_group_id",
                table: "data_files",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_data_files_group_id_is_active_for_filtering",
                table: "data_files",
                columns: new[] { "group_id", "is_active_for_filtering" });

            migrationBuilder.CreateIndex(
                name: "IX_data_files_group_id_sort_order",
                table: "data_files",
                columns: new[] { "group_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_data_files_owner_id",
                table: "data_files",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_data_files_owner_id_is_active_for_filtering",
                table: "data_files",
                columns: new[] { "owner_id", "is_active_for_filtering" });

            migrationBuilder.CreateIndex(
                name: "IX_data_files_owner_id_sort_order",
                table: "data_files",
                columns: new[] { "owner_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_groups_admin_id",
                table: "groups",
                column: "admin_id",
                unique: true,
                filter: "admin_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_groups_last_shared_referral_job_id",
                table: "groups",
                column: "last_shared_referral_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_historical_plates_data_file_id",
                table: "historical_plates",
                column: "data_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_historical_plates_normalized_plate_data_file_id",
                table: "historical_plates",
                columns: new[] { "normalized_plate", "data_file_id" });

            migrationBuilder.CreateIndex(
                name: "IX_historical_plates_user_id",
                table: "historical_plates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_check_session_id_checked_at",
                table: "live_check_logs",
                columns: new[] { "check_session_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_group_id",
                table: "live_check_logs",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_is_matched",
                table: "live_check_logs",
                column: "is_matched");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_matched_check_plate_id",
                table: "live_check_logs",
                column: "matched_check_plate_id");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_normalized_plate",
                table: "live_check_logs",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_live_check_logs_user_id_checked_at",
                table: "live_check_logs",
                columns: new[] { "user_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_check_file_id",
                table: "manual_checks",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_matched_check_plate_id",
                table: "manual_checks",
                column: "matched_check_plate_id");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_normalized_plate",
                table: "manual_checks",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_user_id_checked_at",
                table: "manual_checks",
                columns: new[] { "user_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_checks_user_id_is_matched",
                table: "manual_checks",
                columns: new[] { "user_id", "is_matched" });

            migrationBuilder.CreateIndex(
                name: "IX_match_results_historical_plate_id",
                table: "match_results",
                column: "historical_plate_id");

            migrationBuilder.CreateIndex(
                name: "IX_match_results_referral_job_id_normalized_plate",
                table: "match_results",
                columns: new[] { "referral_job_id", "normalized_plate" });

            migrationBuilder.CreateIndex(
                name: "IX_match_results_source_data_file_id",
                table: "match_results",
                column: "source_data_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_recorded_plates_matched_check_plate_id",
                table: "recorded_plates",
                column: "matched_check_plate_id");

            migrationBuilder.CreateIndex(
                name: "IX_recorded_plates_normalized_plate",
                table: "recorded_plates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_recorded_plates_session_id_is_matched",
                table: "recorded_plates",
                columns: new[] { "session_id", "is_matched" });

            migrationBuilder.CreateIndex(
                name: "IX_recorded_plates_user_id",
                table: "recorded_plates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_normalized_plate",
                table: "recording_archive_plates",
                column: "normalized_plate");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_source_session_id",
                table: "recording_archive_plates",
                column: "source_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_user_id",
                table: "recording_archive_plates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_archive_plates_user_id_appended_to_data_file_id",
                table: "recording_archive_plates",
                columns: new[] { "user_id", "appended_to_data_file_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recording_gps_points_session_id_captured_at",
                table: "recording_gps_points",
                columns: new[] { "session_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_recording_sessions_check_file_id",
                table: "recording_sessions",
                column: "check_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_recording_sessions_group_id_started_at",
                table: "recording_sessions",
                columns: new[] { "group_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_recording_sessions_user_id_started_at",
                table: "recording_sessions",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerGroup",
                table: "recording_sessions",
                columns: new[] { "group_id", "display_name" },
                unique: true,
                filter: "group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RecordingSessions_DisplayName_PerSoloUser",
                table: "recording_sessions",
                columns: new[] { "user_id", "display_name" },
                unique: true,
                filter: "group_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_referral_jobs_group_id_created_at",
                table: "referral_jobs",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_referral_jobs_user_id_created_at",
                table: "referral_jobs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_referral_plates_referral_job_id_normalized_plate",
                table: "referral_plates",
                columns: new[] { "referral_job_id", "normalized_plate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_referral_plates_referral_job_id_status",
                table: "referral_plates",
                columns: new[] { "referral_job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id_is_revoked_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "is_revoked", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_RoleId",
                table: "role_claims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_UserId",
                table: "user_claims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_UserId",
                table: "user_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_user_id",
                table: "user_permissions",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_users_group_id",
                table: "users",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_groups_group_id",
                table: "audit_logs",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_users_user_id",
                table: "audit_logs",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_check_files_groups_group_id",
                table: "check_files",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_check_files_users_uploaded_by",
                table: "check_files",
                column: "uploaded_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_check_sessions_groups_group_id",
                table: "check_sessions",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_check_sessions_users_user_id",
                table: "check_sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_data_files_groups_group_id",
                table: "data_files",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_data_files_users_owner_id",
                table: "data_files",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_referral_jobs_last_shared_referral_job_id",
                table: "groups",
                column: "last_shared_referral_job_id",
                principalTable: "referral_jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

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
                name: "FK_referral_jobs_groups_group_id",
                table: "referral_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_users_groups_group_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "certificates");

            migrationBuilder.DropTable(
                name: "live_check_logs");

            migrationBuilder.DropTable(
                name: "manual_checks");

            migrationBuilder.DropTable(
                name: "match_results");

            migrationBuilder.DropTable(
                name: "recorded_plates");

            migrationBuilder.DropTable(
                name: "recording_archive_plates");

            migrationBuilder.DropTable(
                name: "recording_gps_points");

            migrationBuilder.DropTable(
                name: "referral_plates");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_permissions");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "check_sessions");

            migrationBuilder.DropTable(
                name: "historical_plates");

            migrationBuilder.DropTable(
                name: "check_plates");

            migrationBuilder.DropTable(
                name: "recording_sessions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "data_files");

            migrationBuilder.DropTable(
                name: "check_files");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "referral_jobs");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
