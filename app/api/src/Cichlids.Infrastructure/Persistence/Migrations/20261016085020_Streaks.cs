using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Streaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "captured_in_app",
                schema: "public",
                table: "tank_media",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "care_plan_rule_change",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    rhythm_days = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_plan_rule_change", x => x.id);
                    table.CheckConstraint("ck_care_plan_rule_change_kind", "kind IN ('water_change', 'measurement')");
                    table.CheckConstraint("ck_care_plan_rule_change_rhythm", "rhythm_days BETWEEN 1 AND 90");
                    table.ForeignKey(
                        name: "fk_care_plan_rule_change_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "streak_freeze",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streak_freeze", x => x.id);
                    table.CheckConstraint("ck_streak_freeze_period", "period_end >= period_start");
                    table.CheckConstraint("ck_streak_freeze_year", "year = EXTRACT(YEAR FROM period_end)");
                    table.ForeignKey(
                        name: "fk_streak_freeze_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_streak_freeze_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vacation",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacation", x => x.id);
                    table.CheckConstraint("ck_vacation_dates", "end_date IS NULL OR end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_vacation_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO public.care_plan_rule_change (tank_id, kind, rhythm_days, enabled, at)
                SELECT tank_id, kind, rhythm_days, enabled, TIMESTAMPTZ '2000-01-01 00:00:00+00'
                FROM public.care_plan_rule
                """);

            migrationBuilder.CreateIndex(
                name: "ix_care_plan_rule_change_tank_id_kind_at",
                schema: "public",
                table: "care_plan_rule_change",
                columns: new[] { "tank_id", "kind", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_streak_freeze_care_entry_id",
                schema: "public",
                table: "streak_freeze",
                column: "care_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_streak_freeze_tank_id_year",
                schema: "public",
                table: "streak_freeze",
                columns: new[] { "tank_id", "year" });

            migrationBuilder.CreateIndex(
                name: "ix_vacation_profile_id_start_date",
                schema: "public",
                table: "vacation",
                columns: new[] { "profile_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ux_vacation_active",
                schema: "public",
                table: "vacation",
                column: "profile_id",
                unique: true,
                filter: "end_date IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_plan_rule_change",
                schema: "public");

            migrationBuilder.DropTable(
                name: "streak_freeze",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vacation",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "captured_in_app",
                schema: "public",
                table: "tank_media");
        }
    }
}
