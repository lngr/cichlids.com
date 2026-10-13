using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiscussionPostAuthorOrPosterName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A post attributed to a profile has no poster name of its own.
            migrationBuilder.Sql(
                "UPDATE discussion_post SET poster_name = NULL WHERE author_profile_id IS NOT NULL AND poster_name IS NOT NULL");
            migrationBuilder.AddCheckConstraint(
                name: "ck_discussion_post_author_or_poster_name",
                schema: "public",
                table: "discussion_post",
                sql: "author_profile_id IS NULL OR poster_name IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discussion_post_author_or_poster_name",
                schema: "public",
                table: "discussion_post");
        }
    }
}
