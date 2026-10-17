using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InhabitantStocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inhabitant_tanks_tank_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropIndex(
                name: "ix_inhabitant_tank_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.AddColumn<DateOnly>(
                name: "moved_in_on",
                schema: "public",
                table: "inhabitant",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "public",
                table: "inhabitant",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            // The owner of existing rows is the owner of their tank.
            migrationBuilder.AddColumn<long>(
                name: "owner_profile_id",
                schema: "public",
                table: "inhabitant",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.inhabitant i SET owner_profile_id = t.profile_id FROM public.tank t WHERE t.id = i.tank_id;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "owner_profile_id",
                schema: "public",
                table: "inhabitant",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "removal_reason",
                schema: "public",
                table: "inhabitant",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "removed_on",
                schema: "public",
                table: "inhabitant",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_owner_profile_id",
                schema: "public",
                table: "inhabitant",
                column: "owner_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_tank_id_removed_on",
                schema: "public",
                table: "inhabitant",
                columns: new[] { "tank_id", "removed_on" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_inhabitant_dates",
                schema: "public",
                table: "inhabitant",
                sql: "removed_on IS NULL OR moved_in_on IS NULL OR removed_on >= moved_in_on");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inhabitant_name_count",
                schema: "public",
                table: "inhabitant",
                sql: "name IS NULL OR (count IS NOT NULL AND count = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inhabitant_name_not_blank",
                schema: "public",
                table: "inhabitant",
                sql: "name IS NULL OR length(btrim(name)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inhabitant_removal",
                schema: "public",
                table: "inhabitant",
                sql: "(removed_on IS NULL) = (removal_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inhabitant_removal_reason",
                schema: "public",
                table: "inhabitant",
                sql: "removal_reason IS NULL OR removal_reason IN ('died', 'given_away', 'moved')");

            migrationBuilder.AddForeignKey(
                name: "fk_inhabitant_profiles_owner_profile_id",
                schema: "public",
                table: "inhabitant",
                column: "owner_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inhabitant_tanks_tank_id",
                schema: "public",
                table: "inhabitant",
                column: "tank_id",
                principalSchema: "public",
                principalTable: "tank",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inhabitant_profiles_owner_profile_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropForeignKey(
                name: "fk_inhabitant_tanks_tank_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropIndex(
                name: "ix_inhabitant_owner_profile_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropIndex(
                name: "ix_inhabitant_tank_id_removed_on",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inhabitant_dates",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inhabitant_name_count",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inhabitant_name_not_blank",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inhabitant_removal",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inhabitant_removal_reason",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropColumn(
                name: "moved_in_on",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropColumn(
                name: "owner_profile_id",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropColumn(
                name: "removal_reason",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.DropColumn(
                name: "removed_on",
                schema: "public",
                table: "inhabitant");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_tank_id",
                schema: "public",
                table: "inhabitant",
                column: "tank_id");

            migrationBuilder.AddForeignKey(
                name: "fk_inhabitant_tanks_tank_id",
                schema: "public",
                table: "inhabitant",
                column: "tank_id",
                principalSchema: "public",
                principalTable: "tank",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
