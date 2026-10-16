using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankTimeLapseSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media",
                sql: "section IN ('showcase', 'decoration', 'technic', 'time_lapse')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_section",
                schema: "public",
                table: "tank_media",
                sql: "section IN ('showcase', 'decoration', 'technic')");
        }
    }
}
