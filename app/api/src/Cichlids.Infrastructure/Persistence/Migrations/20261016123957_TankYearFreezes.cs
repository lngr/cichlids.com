using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankYearFreezes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_streak_freeze_year",
                schema: "public",
                table: "streak_freeze");

            migrationBuilder.RenameColumn(
                name: "year",
                schema: "public",
                table: "streak_freeze",
                newName: "tank_year");

            migrationBuilder.RenameIndex(
                name: "ix_streak_freeze_tank_id_year",
                schema: "public",
                table: "streak_freeze",
                newName: "ix_streak_freeze_tank_id_tank_year");

            // The tank year of a stored freeze counts the calendar anniversaries of the creation date (UTC date)
            // of the tank that lie on or before the period end; periods ending before the creation date belong to year 1.
            migrationBuilder.Sql(@"
                UPDATE public.streak_freeze AS f
                SET tank_year = GREATEST(1, 1 + s.years - CASE WHEN f.period_end < (s.created + s.years * INTERVAL '1 year')::date THEN 1 ELSE 0 END)
                FROM (
                    SELECT t.id AS tank_id, (t.created_at AT TIME ZONE 'UTC')::date AS created,
                           EXTRACT(YEAR FROM f2.period_end)::int - EXTRACT(YEAR FROM (t.created_at AT TIME ZONE 'UTC')::date)::int AS years,
                           f2.id AS freeze_id
                    FROM public.streak_freeze AS f2
                    JOIN public.tank AS t ON t.id = f2.tank_id
                ) AS s
                WHERE s.freeze_id = f.id;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_streak_freeze_tank_year",
                schema: "public",
                table: "streak_freeze",
                sql: "tank_year >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_streak_freeze_tank_year",
                schema: "public",
                table: "streak_freeze");

            migrationBuilder.RenameColumn(
                name: "tank_year",
                schema: "public",
                table: "streak_freeze",
                newName: "year");

            migrationBuilder.RenameIndex(
                name: "ix_streak_freeze_tank_id_tank_year",
                schema: "public",
                table: "streak_freeze",
                newName: "ix_streak_freeze_tank_id_year");

            migrationBuilder.Sql("UPDATE public.streak_freeze SET year = EXTRACT(YEAR FROM period_end)::int;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_streak_freeze_year",
                schema: "public",
                table: "streak_freeze",
                sql: "year = EXTRACT(YEAR FROM period_end)");
        }
    }
}
