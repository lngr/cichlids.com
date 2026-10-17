using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RatingCommentReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "comment_id",
                schema: "public",
                table: "rating",
                type: "bigint",
                nullable: true);

            // Migrated ratings reference the comment with their legacy comment id. Ratings written by
            // the API reference the comment of the same member and target with the same creation
            // moment; one request wrote both rows.
            migrationBuilder.Sql(
                """
                UPDATE public.rating SET comment_id = c.id
                FROM public.comment c
                WHERE c.legacy_id = rating.legacy_comment_id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.rating SET comment_id = paired.comment_id
                FROM (
                    SELECT DISTINCT ON (r.id) r.id AS rating_id, c.id AS comment_id
                    FROM public.rating r
                    JOIN public.comment c
                        ON c.legacy_id IS NULL
                        AND c.author_profile_id = r.profile_id
                        AND c.created_at = r.created_at
                        AND c.story_id IS NOT DISTINCT FROM r.story_id
                        AND c.tank_id IS NOT DISTINCT FROM r.tank_id
                    WHERE r.legacy_comment_id IS NULL AND r.comment_id IS NULL
                    ORDER BY r.id, c.id
                ) paired
                WHERE rating.id = paired.rating_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_rating_comment_id",
                schema: "public",
                table: "rating",
                column: "comment_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_rating_comment_comment_id",
                schema: "public",
                table: "rating",
                column: "comment_id",
                principalSchema: "public",
                principalTable: "comment",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_rating_comment_comment_id",
                schema: "public",
                table: "rating");

            migrationBuilder.DropIndex(
                name: "ix_rating_comment_id",
                schema: "public",
                table: "rating");

            migrationBuilder.DropColumn(
                name: "comment_id",
                schema: "public",
                table: "rating");
        }
    }
}
