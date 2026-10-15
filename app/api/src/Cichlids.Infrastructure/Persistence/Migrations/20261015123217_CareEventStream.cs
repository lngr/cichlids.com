using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareEventStream : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount_input_value = table.Column<decimal>(type: "numeric", nullable: true),
                    amount_input_unit = table.Column<string>(type: "text", nullable: true),
                    amount_liters = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_entry", x => x.id);
                    table.CheckConstraint("ck_care_amount", "(amount_input_value IS NULL AND amount_input_unit IS NULL AND amount_liters IS NULL)\nOR (amount_input_value IS NOT NULL AND amount_input_value > 0 AND amount_input_value <= 1000000\n    AND amount_input_unit IS NOT NULL AND (\n        (amount_input_unit = 'percent' AND amount_input_value <= 100 AND amount_liters IS NULL)\n        OR (amount_input_unit = 'liter' AND amount_liters IS NOT NULL AND amount_liters = amount_input_value)\n        OR (amount_input_unit = 'us_gallon' AND amount_liters IS NOT NULL AND amount_liters > 0)))");
                    table.CheckConstraint("ck_care_kind", "kind IN ('water_change')");
                    table.CheckConstraint("ck_care_time", "occurred_at <= recorded_at + interval '2 minutes'");
                    table.ForeignKey(
                        name: "fk_care_entry_profiles_author_profile_id",
                        column: x => x.author_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_entry_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_entry_author_profile_id",
                schema: "public",
                table: "care_entry",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_entry_tank_id_occurred_at_id",
                schema: "public",
                table: "care_entry",
                columns: new[] { "tank_id", "occurred_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_entry",
                schema: "public");
        }
    }
}
