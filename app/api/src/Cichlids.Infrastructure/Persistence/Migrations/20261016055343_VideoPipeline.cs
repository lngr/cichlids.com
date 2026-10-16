using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.AddColumn<int>(
                name: "duration_ms",
                schema: "public",
                table: "media_item",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "care_video",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_video", x => x.id);
                    table.ForeignKey(
                        name: "fk_care_video_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_care_video_media_items_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_job",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_job", x => x.id);
                    table.CheckConstraint("ck_video_job_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_video_job_status", "status IN ('queued', 'running', 'done', 'failed')");
                    table.ForeignKey(
                        name: "fk_video_job_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_upload",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    original_filename = table.Column<string>(type: "text", nullable: true),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    chunk_size = table.Column<int>(type: "integer", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    container = table.Column<string>(type: "text", nullable: true),
                    multipart_upload_id = table.Column<string>(type: "text", nullable: false),
                    part_e_tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_upload", x => x.id);
                    table.CheckConstraint("ck_video_upload_offset", "\"offset\" >= 0 AND \"offset\" <= byte_size AND byte_size > 0 AND chunk_size > 0");
                    table.CheckConstraint("ck_video_upload_target", "target IN ('tank', 'care_entry')");
                    table.ForeignKey(
                        name: "fk_video_upload_profile_owner_profile_id",
                        column: x => x.owner_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_video_upload_tank_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media",
                sql: "section IN ('showcase', 'decoration', 'technic', 'time_lapse', 'video')");

            migrationBuilder.CreateIndex(
                name: "ix_care_video_care_entry_id",
                schema: "public",
                table: "care_video",
                column: "care_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_video_media_item_id",
                schema: "public",
                table: "care_video",
                column: "media_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_video_job_media_item_id",
                schema: "public",
                table: "video_job",
                column: "media_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_video_job_status_run_after",
                schema: "public",
                table: "video_job",
                columns: new[] { "status", "run_after" });

            migrationBuilder.CreateIndex(
                name: "ix_video_upload_expires_at",
                schema: "public",
                table: "video_upload",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_video_upload_owner_profile_id",
                schema: "public",
                table: "video_upload",
                column: "owner_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_video_upload_tank_id",
                schema: "public",
                table: "video_upload",
                column: "tank_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_video",
                schema: "public");

            migrationBuilder.DropTable(
                name: "video_job",
                schema: "public");

            migrationBuilder.DropTable(
                name: "video_upload",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                schema: "public",
                table: "media_item");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media",
                sql: "section IN ('showcase', 'decoration', 'technic', 'time_lapse')");
        }
    }
}
