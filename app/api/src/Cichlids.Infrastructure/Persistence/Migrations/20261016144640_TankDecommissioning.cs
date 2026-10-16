using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankDecommissioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.CreateTable(
                name: "tank_decommissioning",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_media_item_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank_decommissioning", x => x.id);
                    table.CheckConstraint("ck_tank_decommissioning_end", "(ended_at IS NULL AND end_date IS NULL AND end_media_item_id IS NULL) OR (ended_at IS NOT NULL AND end_date IS NOT NULL AND end_media_item_id IS NOT NULL AND end_date >= start_date)");
                    table.CheckConstraint("ck_tank_decommissioning_scope", "scope IN ('all_care', 'water_change')");
                    table.ForeignKey(
                        name: "fk_tank_decommissioning_media_item_end_media_item_id",
                        column: x => x.end_media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tank_decommissioning_media_item_start_media_item_id",
                        column: x => x.start_media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tank_decommissioning_tank_tank_id",
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
                sql: "section IN ('showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission')");

            migrationBuilder.CreateIndex(
                name: "ix_tank_decommissioning_end_media_item_id",
                schema: "public",
                table: "tank_decommissioning",
                column: "end_media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_decommissioning_start_media_item_id",
                schema: "public",
                table: "tank_decommissioning",
                column: "start_media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_decommissioning_tank_id_started_at",
                schema: "public",
                table: "tank_decommissioning",
                columns: new[] { "tank_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_tank_decommissioning_active",
                schema: "public",
                table: "tank_decommissioning",
                column: "tank_id",
                unique: true,
                filter: "ended_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tank_decommissioning",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media",
                sql: "section IN ('showcase', 'decoration', 'technic', 'time_lapse', 'video')");
        }
    }
}
