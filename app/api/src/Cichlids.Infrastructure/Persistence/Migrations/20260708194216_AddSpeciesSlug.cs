using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeciesSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "public",
                table: "species",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_species_slug",
                schema: "public",
                table: "species",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_species_slug",
                schema: "public",
                table: "species");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "public",
                table: "species");
        }
    }
}
