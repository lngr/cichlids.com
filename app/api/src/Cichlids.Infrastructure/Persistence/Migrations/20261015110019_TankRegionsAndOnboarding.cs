using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankRegionsAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adopted_at",
                schema: "public",
                table: "tank",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "region",
                schema: "public",
                table: "tank",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "badges",
                schema: "public",
                table: "profile",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "interests",
                schema: "public",
                table: "profile",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onboarding_completed_at",
                schema: "public",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.tank SET region = CASE category
                    WHEN 'malawi' THEN 'lake_malawi'
                    WHEN 'tanganyika' THEN 'lake_tanganyika'
                    WHEN 'african' THEN 'africa_other'
                    WHEN 'community' THEN 'mixed'
                    WHEN 'central_american' THEN 'central_america'
                    WHEN 'south_american' THEN 'south_america'
                    WHEN 'american' THEN 'americas_unassigned'
                    ELSE NULL END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_region",
                schema: "public",
                table: "tank",
                sql: "region IS NULL OR region IN ('lake_malawi', 'lake_tanganyika', 'lake_victoria', 'africa_other', 'central_america', 'south_america', 'mixed', 'americas_unassigned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_region",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "adopted_at",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "region",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "badges",
                schema: "public",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "interests",
                schema: "public",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "onboarding_completed_at",
                schema: "public",
                table: "profile");
        }
    }
}
