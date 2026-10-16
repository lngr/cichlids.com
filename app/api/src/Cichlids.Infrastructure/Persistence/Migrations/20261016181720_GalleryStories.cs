using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GalleryStories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER post_changes_media_visibility ON public.post;
                DROP FUNCTION public.post_changes_media_visibility();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_collection_entry_posts_post_id",
                schema: "public",
                table: "collection_entry");

            migrationBuilder.DropForeignKey(
                name: "fk_comment_posts_post_id",
                schema: "public",
                table: "comment");

            migrationBuilder.DropForeignKey(
                name: "fk_media_link_posts_post_id",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropForeignKey(
                name: "fk_rating_post_post_id",
                schema: "public",
                table: "rating");

            migrationBuilder.DropForeignKey(
                name: "fk_slug_alias_post_post_id",
                schema: "public",
                table: "slug_alias");

            migrationBuilder.DropIndex(
                name: "ux_rating_profile_post",
                schema: "public",
                table: "rating");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rating_exactly_one_target",
                schema: "public",
                table: "rating");

            migrationBuilder.DropIndex(
                name: "ux_media_link_post_role_item",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_context_key",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_context_type",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comment_exactly_one_target",
                schema: "public",
                table: "comment");

            migrationBuilder.RenameColumn(
                name: "post_id",
                schema: "public",
                table: "slug_alias",
                newName: "story_id");

            migrationBuilder.RenameIndex(
                name: "ix_slug_alias_post_id",
                schema: "public",
                table: "slug_alias",
                newName: "ix_slug_alias_story_id");

            migrationBuilder.RenameColumn(
                name: "post_id",
                schema: "public",
                table: "rating",
                newName: "story_id");

            migrationBuilder.RenameIndex(
                name: "ix_rating_post_id",
                schema: "public",
                table: "rating",
                newName: "ix_rating_story_id");

            migrationBuilder.RenameColumn(
                name: "post_id",
                schema: "public",
                table: "media_link",
                newName: "story_id");

            migrationBuilder.RenameColumn(
                name: "post_id",
                schema: "public",
                table: "comment",
                newName: "story_id");

            migrationBuilder.RenameIndex(
                name: "ix_comment_post_id",
                schema: "public",
                table: "comment",
                newName: "ix_comment_story_id");

            migrationBuilder.RenameColumn(
                name: "post_id",
                schema: "public",
                table: "collection_entry",
                newName: "story_id");

            migrationBuilder.RenameIndex(
                name: "ix_collection_entry_post_id",
                schema: "public",
                table: "collection_entry",
                newName: "ix_collection_entry_story_id");

            migrationBuilder.RenameIndex(
                name: "ix_collection_entry_collection_id_post_id",
                schema: "public",
                table: "collection_entry",
                newName: "ix_collection_entry_collection_id_story_id");

            migrationBuilder.CreateTable(
                name: "story",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    cover_media_link_id = table.Column<long>(type: "bigint", nullable: true),
                    topic = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    view_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    rating_average = table.Column<double>(type: "double precision", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delete_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_by_profile_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_story", x => x.id);
                    table.CheckConstraint("ck_story_state", "state IN ('draft', 'published', 'archived')");
                    table.CheckConstraint("ck_story_topic", "topic IN ('cichlids', 'tanks', 'offtopic', 'contest', 'unknown')");
                    table.ForeignKey(
                        name: "fk_story_media_link_cover_media_link_id",
                        column: x => x.cover_media_link_id,
                        principalSchema: "public",
                        principalTable: "media_link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_story_profile_author_profile_id",
                        column: x => x.author_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_story_profile_deleted_by_profile_id",
                        column: x => x.deleted_by_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_subject",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    story_id = table.Column<long>(type: "bigint", nullable: false),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    inhabitant_id = table.Column<long>(type: "bigint", nullable: true),
                    species_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_story_subject", x => x.id);
                    table.CheckConstraint("ck_story_subject_key", "num_nonnulls(tank_id, inhabitant_id, species_id) = 1 AND (\n    (subject_type = 'tank' AND tank_id IS NOT NULL)\n    OR (subject_type = 'inhabitant' AND inhabitant_id IS NOT NULL)\n    OR (subject_type = 'species' AND species_id IS NOT NULL))");
                    table.CheckConstraint("ck_story_subject_position", "position IS NULL OR position >= 0");
                    table.CheckConstraint("ck_story_subject_type", "subject_type IN ('tank', 'inhabitant', 'species')");
                    table.ForeignKey(
                        name: "fk_story_subject_inhabitant_inhabitant_id",
                        column: x => x.inhabitant_id,
                        principalSchema: "public",
                        principalTable: "inhabitant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_story_subject_species_species_id",
                        column: x => x.species_id,
                        principalSchema: "public",
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_story_subject_story_story_id",
                        column: x => x.story_id,
                        principalSchema: "public",
                        principalTable: "story",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_story_subject_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO public.story (id, legacy_id, author_profile_id, topic, title, description, state, view_count,
                    rating_average, rating_count, created_at, updated_at, published_at, deleted_at, delete_reason, deleted_by_profile_id)
                OVERRIDING SYSTEM VALUE
                SELECT id, legacy_id, author_profile_id, topic, title, description, state, view_count,
                    rating_average, rating_count, created_at, updated_at, published_at, deleted_at, delete_reason, deleted_by_profile_id
                FROM public.post;
                SELECT setval(pg_get_serial_sequence('public.story', 'id'), COALESCE(max(id), 1), count(*) > 0) FROM public.story;
                INSERT INTO public.story_subject (story_id, subject_type, tank_id)
                SELECT id, 'tank', tank_id FROM public.post WHERE tank_id IS NOT NULL;
                INSERT INTO public.story_subject (story_id, subject_type, species_id)
                SELECT post_id, 'species', species_id FROM public.post_species ORDER BY post_id, sort, id;
                UPDATE public.media_link SET context_type = 'story' WHERE context_type = 'post';
                """);

            migrationBuilder.DropTable(
                name: "post_species",
                schema: "public");

            migrationBuilder.DropTable(
                name: "post",
                schema: "public");

            migrationBuilder.CreateIndex(
                name: "ux_rating_profile_story",
                schema: "public",
                table: "rating",
                columns: new[] { "profile_id", "story_id" },
                unique: true,
                filter: "profile_id IS NOT NULL AND story_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rating_exactly_one_target",
                schema: "public",
                table: "rating",
                sql: "(story_id IS NULL) <> (tank_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_story_position",
                schema: "public",
                table: "media_link",
                columns: new[] { "story_id", "position" },
                unique: true,
                filter: "story_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_story_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "story_id", "role", "media_item_id" },
                unique: true,
                filter: "story_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_context_key",
                schema: "public",
                table: "media_link",
                sql: "num_nonnulls(tank_id, inhabitant_id, care_entry_id, water_change_session_id, story_id, discussion_post_id, profile_id) = 1 AND (\n    (context_type = 'tank' AND tank_id IS NOT NULL)\n    OR (context_type = 'inhabitant' AND inhabitant_id IS NOT NULL)\n    OR (context_type = 'care_entry' AND care_entry_id IS NOT NULL)\n    OR (context_type = 'water_change_session' AND water_change_session_id IS NOT NULL)\n    OR (context_type = 'story' AND story_id IS NOT NULL)\n    OR (context_type = 'discussion_post' AND discussion_post_id IS NOT NULL)\n    OR (context_type = 'profile' AND profile_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_context_type",
                schema: "public",
                table: "media_link",
                sql: "context_type IN ('tank', 'inhabitant', 'care_entry', 'water_change_session', 'story', 'discussion_post', 'profile')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link",
                sql: "(context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))\nOR (context_type = 'care_entry' AND role IN ('photo', 'video'))\nOR (context_type = 'water_change_session' AND role = 'evidence')\nOR (context_type = 'story' AND role = 'item')\nOR (context_type = 'discussion_post' AND role = 'attachment')\nOR (context_type = 'profile' AND role IN ('avatar', 'header'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comment_exactly_one_target",
                schema: "public",
                table: "comment",
                sql: "(story_id IS NULL) <> (tank_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_story_author_profile_id",
                schema: "public",
                table: "story",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_story_cover_media_link_id",
                schema: "public",
                table: "story",
                column: "cover_media_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_story_deleted_by_profile_id",
                schema: "public",
                table: "story",
                column: "deleted_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_story_legacy_id",
                schema: "public",
                table: "story",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_story_subject_inhabitant_id",
                schema: "public",
                table: "story_subject",
                column: "inhabitant_id",
                filter: "inhabitant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_story_subject_species_id",
                schema: "public",
                table: "story_subject",
                column: "species_id",
                filter: "species_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_story_subject_tank_id",
                schema: "public",
                table: "story_subject",
                column: "tank_id",
                filter: "tank_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_story_subject_story_inhabitant",
                schema: "public",
                table: "story_subject",
                columns: new[] { "story_id", "inhabitant_id" },
                unique: true,
                filter: "inhabitant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_story_subject_story_position",
                schema: "public",
                table: "story_subject",
                columns: new[] { "story_id", "position" },
                unique: true,
                filter: "position IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_story_subject_story_species",
                schema: "public",
                table: "story_subject",
                columns: new[] { "story_id", "species_id" },
                unique: true,
                filter: "species_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_story_subject_story_tank",
                schema: "public",
                table: "story_subject",
                columns: new[] { "story_id", "tank_id" },
                unique: true,
                filter: "tank_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_collection_entry_stories_story_id",
                schema: "public",
                table: "collection_entry",
                column: "story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_stories_story_id",
                schema: "public",
                table: "comment",
                column: "story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_media_link_stories_story_id",
                schema: "public",
                table: "media_link",
                column: "story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rating_stories_story_id",
                schema: "public",
                table: "rating",
                column: "story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_slug_alias_stories_story_id",
                schema: "public",
                table: "slug_alias",
                column: "story_id",
                principalSchema: "public",
                principalTable: "story",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION public.story_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY(SELECT media_item_id FROM public.media_link WHERE story_id = NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER story_changes_media_visibility AFTER UPDATE ON public.story
                    FOR EACH ROW WHEN (OLD.state IS DISTINCT FROM NEW.state OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at
                        OR OLD.author_profile_id IS DISTINCT FROM NEW.author_profile_id)
                    EXECUTE FUNCTION public.story_changes_media_visibility();
                """);

            migrationBuilder.Sql("""
                UPDATE public.outbox_event SET
                    event_type = CASE WHEN event_type = 'post.published' THEN 'story.published' ELSE event_type END,
                    aggregate_type = CASE WHEN aggregate_type = 'post' THEN 'story' ELSE aggregate_type END,
                    payload = CASE WHEN payload ? 'postId'
                        THEN ((payload - 'postId') || jsonb_build_object('storyId', payload -> 'postId')) ELSE payload END
                WHERE event_type = 'post.published' OR aggregate_type = 'post' OR payload ? 'postId';
                UPDATE public.webhook_subscription SET event_types = array_replace(event_types, 'post.published', 'story.published')
                WHERE 'post.published' = ANY(event_types);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER story_changes_media_visibility ON public.story;
                DROP FUNCTION public.story_changes_media_visibility();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_collection_entry_stories_story_id",
                schema: "public",
                table: "collection_entry");

            migrationBuilder.DropForeignKey(
                name: "fk_comment_stories_story_id",
                schema: "public",
                table: "comment");

            migrationBuilder.DropForeignKey(
                name: "fk_media_link_stories_story_id",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropForeignKey(
                name: "fk_rating_stories_story_id",
                schema: "public",
                table: "rating");

            migrationBuilder.DropForeignKey(
                name: "fk_slug_alias_stories_story_id",
                schema: "public",
                table: "slug_alias");

            migrationBuilder.DropIndex(
                name: "ux_rating_profile_story",
                schema: "public",
                table: "rating");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rating_exactly_one_target",
                schema: "public",
                table: "rating");

            migrationBuilder.DropIndex(
                name: "ux_media_link_story_position",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropIndex(
                name: "ux_media_link_story_role_item",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_context_key",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_context_type",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comment_exactly_one_target",
                schema: "public",
                table: "comment");

            migrationBuilder.RenameColumn(
                name: "story_id",
                schema: "public",
                table: "slug_alias",
                newName: "post_id");

            migrationBuilder.RenameIndex(
                name: "ix_slug_alias_story_id",
                schema: "public",
                table: "slug_alias",
                newName: "ix_slug_alias_post_id");

            migrationBuilder.RenameColumn(
                name: "story_id",
                schema: "public",
                table: "rating",
                newName: "post_id");

            migrationBuilder.RenameIndex(
                name: "ix_rating_story_id",
                schema: "public",
                table: "rating",
                newName: "ix_rating_post_id");

            migrationBuilder.RenameColumn(
                name: "story_id",
                schema: "public",
                table: "media_link",
                newName: "post_id");

            migrationBuilder.RenameColumn(
                name: "story_id",
                schema: "public",
                table: "comment",
                newName: "post_id");

            migrationBuilder.RenameIndex(
                name: "ix_comment_story_id",
                schema: "public",
                table: "comment",
                newName: "ix_comment_post_id");

            migrationBuilder.RenameColumn(
                name: "story_id",
                schema: "public",
                table: "collection_entry",
                newName: "post_id");

            migrationBuilder.RenameIndex(
                name: "ix_collection_entry_story_id",
                schema: "public",
                table: "collection_entry",
                newName: "ix_collection_entry_post_id");

            migrationBuilder.RenameIndex(
                name: "ix_collection_entry_collection_id_story_id",
                schema: "public",
                table: "collection_entry",
                newName: "ix_collection_entry_collection_id_post_id");

            migrationBuilder.CreateTable(
                name: "post",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delete_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rating_average = table.Column<double>(type: "double precision", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    state = table.Column<string>(type: "text", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    topic = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    view_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post", x => x.id);
                    table.CheckConstraint("ck_post_kind", "kind IN ('single', 'story')");
                    table.CheckConstraint("ck_post_state", "state IN ('draft', 'published', 'archived')");
                    table.CheckConstraint("ck_post_topic", "topic IN ('cichlids', 'tanks', 'offtopic', 'contest', 'unknown')");
                    table.ForeignKey(
                        name: "fk_post_profiles_author_profile_id",
                        column: x => x.author_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_post_profiles_deleted_by_profile_id",
                        column: x => x.deleted_by_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_post_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post_species",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    species_id = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.Sql("""
                INSERT INTO public.post (id, legacy_id, author_profile_id, tank_id, kind, topic, title, description, state, view_count,
                    rating_average, rating_count, created_at, updated_at, published_at, deleted_at, delete_reason, deleted_by_profile_id)
                OVERRIDING SYSTEM VALUE
                SELECT s.id, s.legacy_id, s.author_profile_id,
                    (SELECT min(j.tank_id) FROM public.story_subject j WHERE j.story_id = s.id AND j.tank_id IS NOT NULL),
                    CASE WHEN (SELECT count(*) FROM public.media_link l WHERE l.post_id = s.id) > 1 THEN 'story' ELSE 'single' END,
                    s.topic, s.title, s.description, s.state, s.view_count, s.rating_average, s.rating_count, s.created_at,
                    s.updated_at, s.published_at, s.deleted_at, s.delete_reason, s.deleted_by_profile_id
                FROM public.story s;
                SELECT setval(pg_get_serial_sequence('public.post', 'id'), COALESCE(max(id), 1), count(*) > 0) FROM public.post;
                INSERT INTO public.post_species (post_id, species_id, sort)
                SELECT story_id, species_id, (row_number() OVER (PARTITION BY story_id ORDER BY position NULLS LAST, id))::int - 1
                FROM public.story_subject WHERE species_id IS NOT NULL;
                UPDATE public.media_link SET context_type = 'post' WHERE context_type = 'story';
                """);

            migrationBuilder.DropTable(
                name: "story_subject",
                schema: "public");

            migrationBuilder.DropTable(
                name: "story",
                schema: "public");

            migrationBuilder.CreateIndex(
                name: "ux_rating_profile_post",
                schema: "public",
                table: "rating",
                columns: new[] { "profile_id", "post_id" },
                unique: true,
                filter: "profile_id IS NOT NULL AND post_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rating_exactly_one_target",
                schema: "public",
                table: "rating",
                sql: "(post_id IS NULL) <> (tank_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_post_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "post_id", "role", "media_item_id" },
                unique: true,
                filter: "post_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_context_key",
                schema: "public",
                table: "media_link",
                sql: "num_nonnulls(tank_id, inhabitant_id, care_entry_id, water_change_session_id, post_id, discussion_post_id, profile_id) = 1 AND (\n    (context_type = 'tank' AND tank_id IS NOT NULL)\n    OR (context_type = 'inhabitant' AND inhabitant_id IS NOT NULL)\n    OR (context_type = 'care_entry' AND care_entry_id IS NOT NULL)\n    OR (context_type = 'water_change_session' AND water_change_session_id IS NOT NULL)\n    OR (context_type = 'post' AND post_id IS NOT NULL)\n    OR (context_type = 'discussion_post' AND discussion_post_id IS NOT NULL)\n    OR (context_type = 'profile' AND profile_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_context_type",
                schema: "public",
                table: "media_link",
                sql: "context_type IN ('tank', 'inhabitant', 'care_entry', 'water_change_session', 'post', 'discussion_post', 'profile')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link",
                sql: "(context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))\nOR (context_type = 'care_entry' AND role IN ('photo', 'video'))\nOR (context_type = 'water_change_session' AND role = 'evidence')\nOR (context_type = 'post' AND role = 'item')\nOR (context_type = 'discussion_post' AND role = 'attachment')\nOR (context_type = 'profile' AND role IN ('avatar', 'header'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comment_exactly_one_target",
                schema: "public",
                table: "comment",
                sql: "(post_id IS NULL) <> (tank_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_post_author_profile_id",
                schema: "public",
                table: "post",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_deleted_by_profile_id",
                schema: "public",
                table: "post",
                column: "deleted_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_legacy_id",
                schema: "public",
                table: "post",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_tank_id",
                schema: "public",
                table: "post",
                column: "tank_id");

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

            migrationBuilder.AddForeignKey(
                name: "fk_collection_entry_posts_post_id",
                schema: "public",
                table: "collection_entry",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_posts_post_id",
                schema: "public",
                table: "comment",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_media_link_posts_post_id",
                schema: "public",
                table: "media_link",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rating_post_post_id",
                schema: "public",
                table: "rating",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_slug_alias_post_post_id",
                schema: "public",
                table: "slug_alias",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION public.post_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY(SELECT media_item_id FROM public.media_link WHERE post_id = NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER post_changes_media_visibility AFTER UPDATE ON public.post
                    FOR EACH ROW WHEN (OLD.state IS DISTINCT FROM NEW.state OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at
                        OR OLD.author_profile_id IS DISTINCT FROM NEW.author_profile_id)
                    EXECUTE FUNCTION public.post_changes_media_visibility();
                """);

            migrationBuilder.Sql("""
                UPDATE public.outbox_event SET
                    event_type = CASE WHEN event_type = 'story.published' THEN 'post.published' ELSE event_type END,
                    aggregate_type = CASE WHEN aggregate_type = 'story' THEN 'post' ELSE aggregate_type END,
                    payload = CASE WHEN payload ? 'storyId'
                        THEN ((payload - 'storyId') || jsonb_build_object('postId', payload -> 'storyId')) ELSE payload END
                WHERE event_type = 'story.published' OR aggregate_type = 'story' OR payload ? 'storyId';
                UPDATE public.webhook_subscription SET event_types = array_replace(event_types, 'story.published', 'post.published')
                WHERE 'story.published' = ANY(event_types);
                """);
        }
    }
}
