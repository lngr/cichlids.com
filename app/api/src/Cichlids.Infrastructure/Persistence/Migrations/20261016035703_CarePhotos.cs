using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_photo",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_photo", x => x.id);
                    table.CheckConstraint("ck_care_photo_position", "position BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "fk_care_photo_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_care_photo_media_items_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_photo_care_entry_id_position",
                schema: "public",
                table: "care_photo",
                columns: new[] { "care_entry_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_care_photo_media_item_id",
                schema: "public",
                table: "care_photo",
                column: "media_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_photo",
                schema: "public");
        }
    }
}
