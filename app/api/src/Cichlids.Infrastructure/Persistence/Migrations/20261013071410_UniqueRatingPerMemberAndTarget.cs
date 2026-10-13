using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueRatingPerMemberAndTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing duplicates collapse to the newest rating per member and target, and the
            // aggregates of the affected posts are recomputed.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE rating_duplicate ON COMMIT DROP AS
                SELECT r.id, r.post_id
                FROM rating r
                WHERE r.profile_id IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM rating newer
                    WHERE newer.profile_id = r.profile_id
                      AND (newer.post_id = r.post_id OR newer.tank_id = r.tank_id)
                      AND (newer.created_at, newer.id) > (r.created_at, r.id))
                """);
            migrationBuilder.Sql("DELETE FROM rating WHERE id IN (SELECT id FROM rating_duplicate)");
            migrationBuilder.Sql(
                """
                UPDATE post SET rating_count = agg.cnt, rating_average = agg.avg_stars
                FROM (
                    SELECT p.id, COUNT(x.id) AS cnt, AVG(x.stars)::float8 AS avg_stars
                    FROM post p
                    LEFT JOIN rating x ON x.post_id = p.id
                    WHERE p.id IN (SELECT post_id FROM rating_duplicate WHERE post_id IS NOT NULL)
                    GROUP BY p.id
                ) AS agg
                WHERE post.id = agg.id
                """);

            migrationBuilder.DropIndex(
                name: "ix_rating_profile_id",
                schema: "public",
                table: "rating");

            migrationBuilder.CreateIndex(
                name: "ux_rating_profile_post",
                schema: "public",
                table: "rating",
                columns: new[] { "profile_id", "post_id" },
                unique: true,
                filter: "profile_id IS NOT NULL AND post_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rating_profile_tank",
                schema: "public",
                table: "rating",
                columns: new[] { "profile_id", "tank_id" },
                unique: true,
                filter: "profile_id IS NOT NULL AND tank_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rating_profile_post",
                schema: "public",
                table: "rating");

            migrationBuilder.DropIndex(
                name: "ux_rating_profile_tank",
                schema: "public",
                table: "rating");

            migrationBuilder.CreateIndex(
                name: "ix_rating_profile_id",
                schema: "public",
                table: "rating",
                column: "profile_id");
        }
    }
}
