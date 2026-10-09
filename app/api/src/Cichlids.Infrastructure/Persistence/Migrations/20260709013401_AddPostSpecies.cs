using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostSpecies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_species",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    species_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_species", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_species_posts_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_species_species_species_id",
                        column: x => x.species_id,
                        principalSchema: "public",
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_species_post_id_species_id",
                schema: "public",
                table: "post_species",
                columns: new[] { "post_id", "species_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_species_species_id",
                schema: "public",
                table: "post_species",
                column: "species_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_species",
                schema: "public");
        }
    }
}
