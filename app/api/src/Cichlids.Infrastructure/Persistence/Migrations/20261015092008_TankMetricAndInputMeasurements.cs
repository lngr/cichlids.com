using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankMetricAndInputMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "width_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "height_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "depth_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "depth_input_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_input_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "volume_input_unit",
                schema: "public",
                table: "tank",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "volume_input_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "volume_liters",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_input_value",
                schema: "public",
                table: "tank",
                type: "numeric",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.tank
                SET width_input_value = width_value,
                    height_input_value = height_value,
                    depth_input_value = depth_value,
                    width_value = width_value * CASE WHEN dimension_unit = 'inch' THEN 2.54 ELSE 1 END,
                    height_value = height_value * CASE WHEN dimension_unit = 'inch' THEN 2.54 ELSE 1 END,
                    depth_value = depth_value * CASE WHEN dimension_unit = 'inch' THEN 2.54 ELSE 1 END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_volume_input_unit",
                schema: "public",
                table: "tank",
                sql: "volume_input_unit IS NULL OR volume_input_unit IN ('liter', 'us_gallon')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_volume_input_unit",
                schema: "public",
                table: "tank");

            migrationBuilder.Sql("""
                UPDATE public.tank
                SET width_value = width_input_value,
                    height_value = height_input_value,
                    depth_value = depth_input_value;
                """);

            migrationBuilder.DropColumn(
                name: "depth_input_value",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "height_input_value",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "volume_input_unit",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "volume_input_value",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "volume_liters",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "width_input_value",
                schema: "public",
                table: "tank");

            migrationBuilder.AlterColumn<int>(
                name: "width_value",
                schema: "public",
                table: "tank",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "height_value",
                schema: "public",
                table: "tank",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "depth_value",
                schema: "public",
                table: "tank",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
