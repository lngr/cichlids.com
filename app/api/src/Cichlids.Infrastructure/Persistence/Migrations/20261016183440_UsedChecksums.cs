using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsedChecksums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "used_checksum",
                schema: "public",
                columns: table => new
                {
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    checksum_sha256 = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    first_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_used_checksum", x => new { x.profile_id, x.checksum_sha256, x.kind });
                    table.CheckConstraint("ck_used_checksum_kind", "kind IN ('water_change_evidence', 'proof')");
                    table.ForeignKey(
                        name: "fk_used_checksum_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Photos that already serve as proof keep counting as used.
            migrationBuilder.Sql("""
                INSERT INTO public.used_checksum (profile_id, checksum_sha256, kind, first_used_at)
                SELECT m.owner_profile_id, m.checksum_sha256, 'water_change_evidence', min(l.created_at)
                FROM public.media_link l
                JOIN public.media_item m ON m.id = l.media_item_id
                JOIN public.water_change_session s ON s.id = l.water_change_session_id AND s.finished_at IS NOT NULL
                WHERE l.context_type = 'water_change_session' AND m.owner_profile_id IS NOT NULL AND m.checksum_sha256 IS NOT NULL
                GROUP BY m.owner_profile_id, m.checksum_sha256;

                INSERT INTO public.used_checksum (profile_id, checksum_sha256, kind, first_used_at)
                SELECT m.owner_profile_id, m.checksum_sha256, 'proof', min(l.created_at)
                FROM public.media_link l
                JOIN public.media_item m ON m.id = l.media_item_id
                WHERE ((l.context_type = 'tank' AND l.role IN ('decommission', 'recommission'))
                        OR (l.context_type = 'care_entry' AND l.role = 'photo'))
                    AND m.owner_profile_id IS NOT NULL AND m.checksum_sha256 IS NOT NULL
                GROUP BY m.owner_profile_id, m.checksum_sha256;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "used_checksum",
                schema: "public");
        }
    }
}
