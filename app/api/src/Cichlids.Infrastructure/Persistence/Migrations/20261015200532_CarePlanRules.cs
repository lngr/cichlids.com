using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarePlanRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_plan_rule",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    rhythm_days = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reason_code = table.Column<string>(type: "text", nullable: false),
                    reason_parameters_json = table.Column<string>(type: "jsonb", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_plan_rule", x => x.id);
                    table.CheckConstraint("ck_care_plan_kind", "kind IN ('water_change', 'measurement')");
                    table.CheckConstraint("ck_care_plan_reason", "reason_code ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_care_plan_reason_parameters", "reason_parameters_json IS NULL OR jsonb_typeof(reason_parameters_json) = 'object'");
                    table.CheckConstraint("ck_care_plan_rhythm", "rhythm_days BETWEEN 1 AND 90");
                    table.ForeignKey(
                        name: "fk_care_plan_rule_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_plan_rule_tank_id_kind",
                schema: "public",
                table: "care_plan_rule",
                columns: new[] { "tank_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_plan_rule",
                schema: "public");
        }
    }
}
