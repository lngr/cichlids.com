using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "captured_in_app",
                schema: "public",
                table: "media_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                schema: "public",
                table: "media_item",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "media_link",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    context_type = table.Column<string>(type: "text", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    inhabitant_id = table.Column<long>(type: "bigint", nullable: true),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    water_change_session_id = table.Column<long>(type: "bigint", nullable: true),
                    post_id = table.Column<long>(type: "bigint", nullable: true),
                    discussion_post_id = table.Column<long>(type: "bigint", nullable: true),
                    profile_id = table.Column<long>(type: "bigint", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    caption = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_link", x => x.id);
                    table.CheckConstraint("ck_media_link_context_key", "num_nonnulls(tank_id, inhabitant_id, care_entry_id, water_change_session_id, post_id, discussion_post_id, profile_id) = 1 AND (\n    (context_type = 'tank' AND tank_id IS NOT NULL)\n    OR (context_type = 'inhabitant' AND inhabitant_id IS NOT NULL)\n    OR (context_type = 'care_entry' AND care_entry_id IS NOT NULL)\n    OR (context_type = 'water_change_session' AND water_change_session_id IS NOT NULL)\n    OR (context_type = 'post' AND post_id IS NOT NULL)\n    OR (context_type = 'discussion_post' AND discussion_post_id IS NOT NULL)\n    OR (context_type = 'profile' AND profile_id IS NOT NULL))");
                    table.CheckConstraint("ck_media_link_context_type", "context_type IN ('tank', 'inhabitant', 'care_entry', 'water_change_session', 'post', 'discussion_post', 'profile')");
                    table.CheckConstraint("ck_media_link_position", "position >= 0\nAND (NOT (context_type = 'care_entry' AND role = 'photo') OR position < 6)\nAND (context_type <> 'water_change_session' OR position < 6)");
                    table.CheckConstraint("ck_media_link_role", "(context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))\nOR (context_type = 'care_entry' AND role IN ('photo', 'video'))\nOR (context_type = 'water_change_session' AND role = 'evidence')\nOR (context_type = 'post' AND role = 'item')\nOR (context_type = 'discussion_post' AND role = 'attachment')\nOR (context_type = 'profile' AND role IN ('avatar', 'header'))");
                    table.ForeignKey(
                        name: "fk_media_link_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_discussion_post_discussion_post_id",
                        column: x => x.discussion_post_id,
                        principalSchema: "public",
                        principalTable: "discussion_post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_inhabitant_inhabitant_id",
                        column: x => x.inhabitant_id,
                        principalSchema: "public",
                        principalTable: "inhabitant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_posts_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_link_water_change_sessions_water_change_session_id",
                        column: x => x.water_change_session_id,
                        principalSchema: "public",
                        principalTable: "water_change_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_link_media_item_id",
                schema: "public",
                table: "media_link",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_care_entry_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "care_entry_id", "role", "media_item_id" },
                unique: true,
                filter: "care_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_care_entry_role_position",
                schema: "public",
                table: "media_link",
                columns: new[] { "care_entry_id", "role", "position" },
                unique: true,
                filter: "care_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_discussion_post_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "discussion_post_id", "role", "media_item_id" },
                unique: true,
                filter: "discussion_post_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_inhabitant_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "inhabitant_id", "role", "media_item_id" },
                unique: true,
                filter: "inhabitant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_post_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "post_id", "role", "media_item_id" },
                unique: true,
                filter: "post_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_profile_role",
                schema: "public",
                table: "media_link",
                columns: new[] { "profile_id", "role" },
                unique: true,
                filter: "profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_session_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "water_change_session_id", "role", "media_item_id" },
                unique: true,
                filter: "water_change_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_session_role_position",
                schema: "public",
                table: "media_link",
                columns: new[] { "water_change_session_id", "role", "position" },
                unique: true,
                filter: "water_change_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_tank_main",
                schema: "public",
                table: "media_link",
                column: "tank_id",
                unique: true,
                filter: "role = 'main' AND tank_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_media_link_tank_role_item",
                schema: "public",
                table: "media_link",
                columns: new[] { "tank_id", "role", "media_item_id" },
                unique: true,
                filter: "tank_id IS NOT NULL");

            migrationBuilder.Sql("""
                UPDATE public.media_item SET received_at = created_at;

                UPDATE public.media_item m SET captured_in_app = true
                FROM public.tank_media t WHERE t.media_item_id = m.id AND t.captured_in_app;

                UPDATE public.media_item m SET captured_in_app = p.captured_in_app OR m.captured_in_app, received_at = p.received_at
                FROM public.water_change_session_photo p WHERE p.media_item_id = m.id;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, tank_id)
                SELECT media_item_id, 'tank', section, sort, tank_id FROM public.tank_media;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, tank_id)
                SELECT main_media_id, 'tank', 'main', 0, id FROM public.tank WHERE main_media_id IS NOT NULL;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, post_id)
                SELECT media_item_id, 'post', 'item', sort, post_id FROM public.post_media;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, care_entry_id)
                SELECT media_item_id, 'care_entry', 'photo', position, care_entry_id FROM public.care_photo;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, care_entry_id)
                SELECT media_item_id, 'care_entry', 'video',
                    (row_number() OVER (PARTITION BY care_entry_id ORDER BY id) - 1)::int, care_entry_id
                FROM public.care_video;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, water_change_session_id)
                SELECT media_item_id, 'water_change_session', 'evidence', position, session_id FROM public.water_change_session_photo;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, discussion_post_id)
                SELECT media_item_id, 'discussion_post', 'attachment', sort, discussion_post_id FROM public.discussion_post_media;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, profile_id)
                SELECT avatar_media_id, 'profile', 'avatar', 0, id FROM public.profile WHERE avatar_media_id IS NOT NULL;

                INSERT INTO public.media_link (media_item_id, context_type, role, position, profile_id)
                SELECT profile_image_media_id, 'profile', 'header', 0, id FROM public.profile WHERE profile_image_media_id IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_profile_media_item_avatar_media_id",
                schema: "public",
                table: "profile");

            migrationBuilder.DropForeignKey(
                name: "fk_profile_media_item_profile_image_media_id",
                schema: "public",
                table: "profile");

            migrationBuilder.DropForeignKey(
                name: "fk_tank_media_item_main_media_id",
                schema: "public",
                table: "tank");

            migrationBuilder.DropTable(
                name: "care_photo",
                schema: "public");

            migrationBuilder.DropTable(
                name: "care_video",
                schema: "public");

            migrationBuilder.DropTable(
                name: "discussion_post_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "post_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tank_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "water_change_session_photo",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_tank_main_media_id",
                schema: "public",
                table: "tank");

            migrationBuilder.DropIndex(
                name: "ix_profile_avatar_media_id",
                schema: "public",
                table: "profile");

            migrationBuilder.DropIndex(
                name: "ix_profile_profile_image_media_id",
                schema: "public",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "main_media_id",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "avatar_media_id",
                schema: "public",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "profile_image_media_id",
                schema: "public",
                table: "profile");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "main_media_id",
                schema: "public",
                table: "tank",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "avatar_media_id",
                schema: "public",
                table: "profile",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_image_media_id",
                schema: "public",
                table: "profile",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "care_photo",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "care_video",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_video", x => x.id);
                    table.ForeignKey(
                        name: "fk_care_video_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_care_video_media_items_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "post_media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_media_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_media_posts_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tank_media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    captured_in_app = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tank_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank_media", x => x.id);
                    table.CheckConstraint("ck_tank_media_section", "section IN ('showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission')");
                    table.ForeignKey(
                        name: "fk_tank_media_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tank_media_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "water_change_session_photo",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    captured_in_app = table.Column<bool>(type: "boolean", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_water_change_session_photo", x => x.id);
                    table.CheckConstraint("ck_water_change_session_photo_position", "position >= 0 AND position < 6");
                    table.ForeignKey(
                        name: "fk_water_change_session_photo_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_water_change_session_photo_water_change_session_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "water_change_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tank_main_media_id",
                schema: "public",
                table: "tank",
                column: "main_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_avatar_media_id",
                schema: "public",
                table: "profile",
                column: "avatar_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_profile_image_media_id",
                schema: "public",
                table: "profile",
                column: "profile_image_media_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_care_video_care_entry_id",
                schema: "public",
                table: "care_video",
                column: "care_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_video_media_item_id",
                schema: "public",
                table: "care_video",
                column: "media_item_id",
                unique: true);

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
                name: "ix_post_media_media_item_id",
                schema: "public",
                table: "post_media",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_media_post_id_media_item_id",
                schema: "public",
                table: "post_media",
                columns: new[] { "post_id", "media_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_media_item_id",
                schema: "public",
                table: "tank_media",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_tank_id_media_item_id_section",
                schema: "public",
                table: "tank_media",
                columns: new[] { "tank_id", "media_item_id", "section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_photo_media_item_id",
                schema: "public",
                table: "water_change_session_photo",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_photo_session_id_position",
                schema: "public",
                table: "water_change_session_photo",
                columns: new[] { "session_id", "position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_profile_media_item_avatar_media_id",
                schema: "public",
                table: "profile",
                column: "avatar_media_id",
                principalSchema: "public",
                principalTable: "media_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_profile_media_item_profile_image_media_id",
                schema: "public",
                table: "profile",
                column: "profile_image_media_id",
                principalSchema: "public",
                principalTable: "media_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tank_media_item_main_media_id",
                schema: "public",
                table: "tank",
                column: "main_media_id",
                principalSchema: "public",
                principalTable: "media_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                INSERT INTO public.tank_media (tank_id, media_item_id, section, sort, captured_in_app)
                SELECT l.tank_id, l.media_item_id, l.role, l.position, m.captured_in_app
                FROM public.media_link l JOIN public.media_item m ON m.id = l.media_item_id
                WHERE l.context_type = 'tank' AND l.role <> 'main';

                UPDATE public.tank t SET main_media_id = l.media_item_id
                FROM public.media_link l WHERE l.tank_id = t.id AND l.context_type = 'tank' AND l.role = 'main';

                INSERT INTO public.post_media (post_id, media_item_id, sort)
                SELECT post_id, media_item_id, position FROM public.media_link WHERE context_type = 'post';

                INSERT INTO public.care_photo (care_entry_id, media_item_id, position)
                SELECT care_entry_id, media_item_id, position FROM public.media_link
                WHERE context_type = 'care_entry' AND role = 'photo'
                ON CONFLICT DO NOTHING;

                INSERT INTO public.care_video (care_entry_id, media_item_id)
                SELECT care_entry_id, media_item_id FROM public.media_link
                WHERE context_type = 'care_entry' AND role = 'video'
                ORDER BY care_entry_id, position
                ON CONFLICT DO NOTHING;

                INSERT INTO public.water_change_session_photo (session_id, media_item_id, position, captured_in_app, received_at)
                SELECT l.water_change_session_id, l.media_item_id, l.position, m.captured_in_app, m.received_at
                FROM public.media_link l JOIN public.media_item m ON m.id = l.media_item_id
                WHERE l.context_type = 'water_change_session';

                INSERT INTO public.discussion_post_media (discussion_post_id, media_item_id, sort)
                SELECT discussion_post_id, media_item_id, position FROM public.media_link WHERE context_type = 'discussion_post';

                UPDATE public.profile p SET avatar_media_id = l.media_item_id
                FROM public.media_link l WHERE l.profile_id = p.id AND l.context_type = 'profile' AND l.role = 'avatar';

                UPDATE public.profile p SET profile_image_media_id = l.media_item_id
                FROM public.media_link l WHERE l.profile_id = p.id AND l.context_type = 'profile' AND l.role = 'header';
                """);

            migrationBuilder.DropTable(
                name: "media_link",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "captured_in_app",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "received_at",
                schema: "public",
                table: "media_item");
        }
    }
}
