using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaCaptureTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "capture_checked_at",
                schema: "public",
                table: "media_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "taken_at_local",
                schema: "public",
                table: "media_item",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "taken_at_offset_minutes",
                schema: "public",
                table: "media_item",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_item_taken_at_offset",
                schema: "public",
                table: "media_item",
                sql: "taken_at_offset_minutes IS NULL OR (taken_at_local IS NOT NULL AND taken_at_offset_minutes BETWEEN -840 AND 840)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_media_item_taken_at_offset",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "capture_checked_at",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "taken_at_local",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "taken_at_offset_minutes",
                schema: "public",
                table: "media_item");
        }
    }
}
