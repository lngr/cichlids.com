using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaPlacementPerItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A tank whose placement was due hands the due placement to its media items.
            migrationBuilder.Sql("""
                UPDATE public.media_item SET placement_pending = true, placement_attempts = 0, placement_next_attempt_at = NULL
                WHERE id IN (
                    SELECT l.media_item_id FROM public.media_link l JOIN public.tank t ON t.id = l.tank_id WHERE t.media_move_pending
                    UNION
                    SELECT l.media_item_id FROM public.media_link l JOIN public.care_entry e ON e.id = l.care_entry_id
                        JOIN public.tank t ON t.id = e.tank_id WHERE t.media_move_pending
                    UNION
                    SELECT l.media_item_id FROM public.media_link l JOIN public.water_change_session s ON s.id = l.water_change_session_id
                        JOIN public.tank t ON t.id = s.tank_id WHERE t.media_move_pending)
                """);

            migrationBuilder.DropIndex(
                name: "ix_tank_media_move_next_attempt_at",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "is_private",
                schema: "public",
                table: "video_job");

            migrationBuilder.DropColumn(
                name: "media_move_attempts",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "media_move_next_attempt_at",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "media_move_pending",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "media_placed_public",
                schema: "public",
                table: "tank");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_private",
                schema: "public",
                table: "video_job",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "media_move_attempts",
                schema: "public",
                table: "tank",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "media_move_next_attempt_at",
                schema: "public",
                table: "tank",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "media_move_pending",
                schema: "public",
                table: "tank",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "media_placed_public",
                schema: "public",
                table: "tank",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_move_next_attempt_at",
                schema: "public",
                table: "tank",
                column: "media_move_next_attempt_at",
                filter: "media_move_pending");
        }
    }
}
