using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankFollowersAndSelectedMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_profile_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_stream_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_stream_within_profile",
                schema: "public",
                table: "tank");

            migrationBuilder.DropIndex(
                name: "ix_follow_target_tank_id",
                schema: "public",
                table: "follow");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "public",
                table: "follow",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "public",
                table: "follow",
                type: "text",
                nullable: false,
                defaultValue: "approved");

            migrationBuilder.CreateTable(
                name: "tank_selected_member",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    member_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank_selected_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_tank_selected_member_profile_member_profile_id",
                        column: x => x.member_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tank_selected_member_tank_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_profile_visibility",
                schema: "public",
                table: "tank",
                sql: "profile_visibility IN ('private', 'selected', 'followers', 'public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_stream_visibility",
                schema: "public",
                table: "tank",
                sql: "stream_visibility IN ('private', 'selected', 'followers', 'public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_stream_within_profile",
                schema: "public",
                table: "tank",
                sql: "(CASE stream_visibility WHEN 'private' THEN 0 WHEN 'selected' THEN 1 WHEN 'followers' THEN 2 ELSE 3 END) <= (CASE profile_visibility WHEN 'private' THEN 0 WHEN 'selected' THEN 1 WHEN 'followers' THEN 2 ELSE 3 END)");

            migrationBuilder.CreateIndex(
                name: "ix_follow_target_tank_id_status",
                schema: "public",
                table: "follow",
                columns: new[] { "target_tank_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_follow_pending_tank",
                schema: "public",
                table: "follow",
                sql: "status = 'approved' OR target_tank_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_follow_status",
                schema: "public",
                table: "follow",
                sql: "status IN ('pending', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_tank_selected_member_member_profile_id",
                schema: "public",
                table: "tank_selected_member",
                column: "member_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_selected_member_tank_id_member_profile_id",
                schema: "public",
                table: "tank_selected_member",
                columns: new[] { "tank_id", "member_profile_id" },
                unique: true);

            // An approved follow and a selected member reach the media of the tank, so adding or removing one
            // changes the visibility generation of the media items of the tank in the same transaction,
            // whichever statement makes the change.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.follow_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE tank bigint;
                BEGIN
                    tank := CASE WHEN TG_OP = 'DELETE' THEN OLD.target_tank_id ELSE NEW.target_tank_id END;
                    PERFORM public.media_item_change_visibility(public.media_item_ids_of_tank(tank));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER follow_inserts_media_visibility AFTER INSERT ON public.follow
                    FOR EACH ROW WHEN (NEW.target_tank_id IS NOT NULL AND NEW.status = 'approved')
                    EXECUTE FUNCTION public.follow_changes_media_visibility();
                CREATE TRIGGER follow_deletes_media_visibility AFTER DELETE ON public.follow
                    FOR EACH ROW WHEN (OLD.target_tank_id IS NOT NULL AND OLD.status = 'approved')
                    EXECUTE FUNCTION public.follow_changes_media_visibility();
                CREATE TRIGGER follow_updates_media_visibility AFTER UPDATE ON public.follow
                    FOR EACH ROW WHEN (NEW.target_tank_id IS NOT NULL AND OLD.status IS DISTINCT FROM NEW.status)
                    EXECUTE FUNCTION public.follow_changes_media_visibility();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.tank_selected_member_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE tank bigint;
                BEGIN
                    tank := CASE WHEN TG_OP = 'DELETE' THEN OLD.tank_id ELSE NEW.tank_id END;
                    PERFORM public.media_item_change_visibility(public.media_item_ids_of_tank(tank));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER tank_selected_member_changes_media_visibility AFTER INSERT OR DELETE ON public.tank_selected_member
                    FOR EACH ROW EXECUTE FUNCTION public.tank_selected_member_changes_media_visibility();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER tank_selected_member_changes_media_visibility ON public.tank_selected_member;
                DROP FUNCTION public.tank_selected_member_changes_media_visibility();
                DROP TRIGGER follow_updates_media_visibility ON public.follow;
                DROP TRIGGER follow_deletes_media_visibility ON public.follow;
                DROP TRIGGER follow_inserts_media_visibility ON public.follow;
                DROP FUNCTION public.follow_changes_media_visibility();
                """);
            migrationBuilder.Sql("""
                DELETE FROM public.follow WHERE status = 'pending';
                UPDATE public.tank SET
                    profile_visibility = CASE WHEN profile_visibility IN ('followers', 'selected') THEN 'private' ELSE profile_visibility END,
                    stream_visibility = CASE WHEN stream_visibility IN ('followers', 'selected') THEN 'private' ELSE stream_visibility END;
                """);
            migrationBuilder.DropTable(
                name: "tank_selected_member",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_profile_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_stream_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_stream_within_profile",
                schema: "public",
                table: "tank");

            migrationBuilder.DropIndex(
                name: "ix_follow_target_tank_id_status",
                schema: "public",
                table: "follow");

            migrationBuilder.DropCheckConstraint(
                name: "ck_follow_pending_tank",
                schema: "public",
                table: "follow");

            migrationBuilder.DropCheckConstraint(
                name: "ck_follow_status",
                schema: "public",
                table: "follow");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "public",
                table: "follow");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "public",
                table: "follow");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_profile_visibility",
                schema: "public",
                table: "tank",
                sql: "profile_visibility IN ('private', 'public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_stream_visibility",
                schema: "public",
                table: "tank",
                sql: "stream_visibility IN ('private', 'public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_stream_within_profile",
                schema: "public",
                table: "tank",
                sql: "stream_visibility = 'private' OR profile_visibility = 'public'");

            migrationBuilder.CreateIndex(
                name: "ix_follow_target_tank_id",
                schema: "public",
                table: "follow",
                column: "target_tank_id");
        }
    }
}
