using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TankVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<string>(
                name: "profile_visibility",
                schema: "public",
                table: "tank",
                type: "text",
                nullable: false,
                defaultValue: "public");

            migrationBuilder.AddColumn<string>(
                name: "stream_visibility",
                schema: "public",
                table: "tank",
                type: "text",
                nullable: false,
                defaultValue: "public");

            migrationBuilder.AddColumn<bool>(
                name: "only_me",
                schema: "public",
                table: "care_entry",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tank_visibility_consent",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    answer = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    context = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank_visibility_consent", x => x.id);
                    table.CheckConstraint("ck_tank_visibility_consent_context", "context IN ('creation', 'settings')");
                    table.ForeignKey(
                        name: "fk_tank_visibility_consent_tank_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_move_next_attempt_at",
                schema: "public",
                table: "tank",
                column: "media_move_next_attempt_at",
                filter: "media_move_pending");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tank_media_move_attempts",
                schema: "public",
                table: "tank",
                sql: "media_move_attempts >= 0");

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
                name: "ix_tank_visibility_consent_tank_id_answered_at_id",
                schema: "public",
                table: "tank_visibility_consent",
                columns: new[] { "tank_id", "answered_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tank_visibility_consent",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_tank_media_move_next_attempt_at",
                schema: "public",
                table: "tank");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tank_media_move_attempts",
                schema: "public",
                table: "tank");

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

            migrationBuilder.DropColumn(
                name: "profile_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "stream_visibility",
                schema: "public",
                table: "tank");

            migrationBuilder.DropColumn(
                name: "only_me",
                schema: "public",
                table: "care_entry");
        }
    }
}
