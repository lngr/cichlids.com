using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WaterChangeSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_seconds",
                schema: "public",
                table: "care_entry",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence",
                schema: "public",
                table: "care_entry",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "evidence_missing",
                schema: "public",
                table: "care_entry",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<long>(
                name: "water_change_session_id",
                schema: "public",
                table: "care_entry",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "water_change_session",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    drained_media_item_id = table.Column<long>(type: "bigint", nullable: true),
                    drained_photo_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    drained_captured_in_app = table.Column<bool>(type: "boolean", nullable: true),
                    refilled_media_item_id = table.Column<long>(type: "bigint", nullable: true),
                    refilled_photo_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refilled_captured_in_app = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_change_session", x => x.id);
                    table.CheckConstraint("ck_water_change_session_finish", "(finished_at IS NULL AND duration_seconds IS NULL)\nOR (finished_at IS NOT NULL AND duration_seconds IS NOT NULL AND duration_seconds >= 0)");
                    table.CheckConstraint("ck_water_change_session_photos", "((drained_photo_at IS NULL) = (drained_captured_in_app IS NULL))\nAND ((refilled_photo_at IS NULL) = (refilled_captured_in_app IS NULL))");
                    table.ForeignKey(
                        name: "fk_water_change_session_media_item_drained_media_item_id",
                        column: x => x.drained_media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_water_change_session_media_item_refilled_media_item_id",
                        column: x => x.refilled_media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_water_change_session_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_water_change_session_tank_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "water_change_session_step",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    step = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_change_session_step", x => x.id);
                    table.CheckConstraint("ck_water_change_session_step_state", "state IN ('done', 'skipped')");
                    table.ForeignKey(
                        name: "fk_water_change_session_step_water_change_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "water_change_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_entry_water_change_session_id",
                schema: "public",
                table: "care_entry",
                column: "water_change_session_id",
                unique: true,
                filter: "water_change_session_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_care_evidence",
                schema: "public",
                table: "care_entry",
                sql: "evidence IS NULL OR (evidence IN ('none', 'partial', 'verified') AND kind = 'water_change' AND water_change_session_id IS NOT NULL AND duration_seconds >= 0)");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_drained_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "drained_media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_profile_id",
                schema: "public",
                table: "water_change_session",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_refilled_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "refilled_media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_tank_id_finished_at",
                schema: "public",
                table: "water_change_session",
                columns: new[] { "tank_id", "finished_at" });

            migrationBuilder.CreateIndex(
                name: "ux_water_change_session_open",
                schema: "public",
                table: "water_change_session",
                column: "tank_id",
                unique: true,
                filter: "finished_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_step_session_id_step",
                schema: "public",
                table: "water_change_session_step",
                columns: new[] { "session_id", "step" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_care_entry_water_change_sessions_water_change_session_id",
                schema: "public",
                table: "care_entry",
                column: "water_change_session_id",
                principalSchema: "public",
                principalTable: "water_change_session",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_care_entry_water_change_sessions_water_change_session_id",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropTable(
                name: "water_change_session_step",
                schema: "public");

            migrationBuilder.DropTable(
                name: "water_change_session",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_care_entry_water_change_session_id",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropCheckConstraint(
                name: "ck_care_evidence",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropColumn(
                name: "evidence",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropColumn(
                name: "evidence_missing",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropColumn(
                name: "water_change_session_id",
                schema: "public",
                table: "care_entry");
        }
    }
}
