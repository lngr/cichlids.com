using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareEntryNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "public",
                table: "care_entry",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_care_note",
                schema: "public",
                table: "care_entry",
                sql: "note IS NULL OR char_length(note) BETWEEN 1 AND 2000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_care_note",
                schema: "public",
                table: "care_entry");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "public",
                table: "care_entry");
        }
    }
}
