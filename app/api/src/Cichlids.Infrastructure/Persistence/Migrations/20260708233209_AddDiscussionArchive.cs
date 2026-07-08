using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscussionArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discussion_thread",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    category = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_post_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    post_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discussion_thread", x => x.id);
                    table.CheckConstraint("ck_discussion_thread_category", "category IN ('cichlids', 'african', 'market_place')");
                    table.CheckConstraint("ck_discussion_thread_state", "state IN ('archived', 'open')");
                });

            migrationBuilder.CreateTable(
                name: "discussion_post",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    poster_name = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discussion_post", x => x.id);
                    table.ForeignKey(
                        name: "fk_discussion_post_discussion_threads_thread_id",
                        column: x => x.thread_id,
                        principalSchema: "public",
                        principalTable: "discussion_thread",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discussion_post_profiles_author_profile_id",
                        column: x => x.author_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discussion_post_media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discussion_post_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discussion_post_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_discussion_post_media_discussion_posts_discussion_post_id",
                        column: x => x.discussion_post_id,
                        principalSchema: "public",
                        principalTable: "discussion_post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discussion_post_media_media_items_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discussion_post_author_profile_id",
                schema: "public",
                table: "discussion_post",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_discussion_post_legacy_id",
                schema: "public",
                table: "discussion_post",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discussion_post_thread_id",
                schema: "public",
                table: "discussion_post",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "ix_discussion_post_media_discussion_post_id_media_item_id",
                schema: "public",
                table: "discussion_post_media",
                columns: new[] { "discussion_post_id", "media_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discussion_post_media_media_item_id",
                schema: "public",
                table: "discussion_post_media",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_discussion_thread_legacy_id",
                schema: "public",
                table: "discussion_thread",
                column: "legacy_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discussion_post_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "discussion_post",
                schema: "public");

            migrationBuilder.DropTable(
                name: "discussion_thread",
                schema: "public");
        }
    }
}
