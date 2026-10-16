using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WaterChangeSessionPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_water_change_session_media_item_drained_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropForeignKey(
                name: "fk_water_change_session_media_item_refilled_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropIndex(
                name: "ix_water_change_session_drained_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropIndex(
                name: "ix_water_change_session_refilled_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropCheckConstraint(
                name: "ck_water_change_session_photos",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "drained_captured_in_app",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "drained_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "drained_photo_at",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "refilled_captured_in_app",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "refilled_media_item_id",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.DropColumn(
                name: "refilled_photo_at",
                schema: "public",
                table: "water_change_session");

            migrationBuilder.CreateTable(
                name: "water_change_session_photo",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    captured_in_app = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "water_change_session_photo",
                schema: "public");

            migrationBuilder.AddColumn<bool>(
                name: "drained_captured_in_app",
                schema: "public",
                table: "water_change_session",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "drained_media_item_id",
                schema: "public",
                table: "water_change_session",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "drained_photo_at",
                schema: "public",
                table: "water_change_session",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "refilled_captured_in_app",
                schema: "public",
                table: "water_change_session",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "refilled_media_item_id",
                schema: "public",
                table: "water_change_session",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refilled_photo_at",
                schema: "public",
                table: "water_change_session",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_drained_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "drained_media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_water_change_session_refilled_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "refilled_media_item_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_water_change_session_photos",
                schema: "public",
                table: "water_change_session",
                sql: "((drained_photo_at IS NULL) = (drained_captured_in_app IS NULL))\nAND ((refilled_photo_at IS NULL) = (refilled_captured_in_app IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "fk_water_change_session_media_item_drained_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "drained_media_item_id",
                principalSchema: "public",
                principalTable: "media_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_water_change_session_media_item_refilled_media_item_id",
                schema: "public",
                table: "water_change_session",
                column: "refilled_media_item_id",
                principalSchema: "public",
                principalTable: "media_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
