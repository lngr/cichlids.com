using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaPlacementQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "placed_public",
                schema: "public",
                table: "media_item",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "placement_attempts",
                schema: "public",
                table: "media_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "placement_next_attempt_at",
                schema: "public",
                table: "media_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "placement_pending",
                schema: "public",
                table: "media_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // An item lies in the private area when any of its variants does, or, while a video has no
            // variants yet, when its transcoding job writes into the private area.
            migrationBuilder.Sql("""
                UPDATE public.media_item m SET placed_public = NOT (
                    EXISTS (SELECT 1 FROM public.media_variant v WHERE v.media_item_id = m.id AND v.storage_key LIKE 'private/%')
                    OR (NOT EXISTS (SELECT 1 FROM public.media_variant v WHERE v.media_item_id = m.id)
                        AND EXISTS (SELECT 1 FROM public.video_job j WHERE j.media_item_id = m.id AND j.is_private)))
                """);

            migrationBuilder.CreateIndex(
                name: "ix_media_item_placement_due",
                schema: "public",
                table: "media_item",
                column: "placement_next_attempt_at",
                filter: "placement_pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_media_item_placement_due",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "placed_public",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "placement_attempts",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "placement_next_attempt_at",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "placement_pending",
                schema: "public",
                table: "media_item");
        }
    }
}
