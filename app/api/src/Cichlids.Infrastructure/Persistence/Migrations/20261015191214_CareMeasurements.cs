using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_care_kind",
                schema: "public",
                table: "care_entry");

            migrationBuilder.CreateTable(
                name: "care_measurement_value",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    parameter = table.Column<string>(type: "text", nullable: false),
                    input_value = table.Column<decimal>(type: "numeric", nullable: false),
                    input_unit = table.Column<string>(type: "text", nullable: false),
                    normalized_value = table.Column<decimal>(type: "numeric", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_measurement_value", x => x.id);
                    table.CheckConstraint("ck_care_measurement_origin", "origin IS NULL OR origin IN ('test_kit', 'meter', 'probe')");
                    table.CheckConstraint("ck_care_measurement_range", "(parameter = 'temperature' AND normalized_value BETWEEN 0 AND 45)\nOR (parameter = 'ph' AND normalized_value BETWEEN 0 AND 14)\nOR (parameter = 'kh' AND normalized_value BETWEEN 0 AND 50)\nOR (parameter = 'gh' AND normalized_value BETWEEN 0 AND 100)\nOR (parameter IN ('no2', 'nh4', 'po4') AND normalized_value BETWEEN 0 AND 100)\nOR (parameter = 'no3' AND normalized_value BETWEEN 0 AND 1000)\nOR (parameter IN ('conductivity', 'tds') AND normalized_value BETWEEN 0 AND 100000)");
                    table.CheckConstraint("ck_care_measurement_unit", "(parameter = 'temperature' AND input_unit IN ('celsius', 'fahrenheit'))\nOR (parameter = 'ph' AND input_unit = 'none')\nOR (parameter = 'kh' AND input_unit IN ('dkh', 'ppm'))\nOR (parameter = 'gh' AND input_unit IN ('dgh', 'ppm'))\nOR (parameter IN ('no2', 'no3', 'nh4', 'po4') AND input_unit = 'mg_per_liter')\nOR (parameter = 'conductivity' AND input_unit = 'microsiemens_per_cm')\nOR (parameter = 'tds' AND input_unit = 'ppm')");
                    table.ForeignKey(
                        name: "fk_care_measurement_value_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_care_kind",
                schema: "public",
                table: "care_entry",
                sql: "kind IN ('water_change', 'measurement')");

            migrationBuilder.CreateIndex(
                name: "ix_care_measurement_value_care_entry_id_parameter",
                schema: "public",
                table: "care_measurement_value",
                columns: new[] { "care_entry_id", "parameter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_measurement_value",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_care_kind",
                schema: "public",
                table: "care_entry");

            migrationBuilder.AddCheckConstraint(
                name: "ck_care_kind",
                schema: "public",
                table: "care_entry",
                sql: "kind IN ('water_change')");
        }
    }
}
