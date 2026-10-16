using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuickShareStory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "quick_share_story_id",
                schema: "public",
                table: "media_item",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_media_item_quick_share_story",
                schema: "public",
                table: "media_item",
                column: "quick_share_story_id",
                unique: true,
                filter: "quick_share_story_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_media_item_stories_quick_share_story_id",
                schema: "public",
                table: "media_item",
                column: "quick_share_story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_media_item_stories_quick_share_story_id",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropIndex(
                name: "ux_media_item_quick_share_story",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "quick_share_story_id",
                schema: "public",
                table: "media_item");
        }
    }
}
