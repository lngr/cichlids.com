using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileHandleChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handle_changed_at",
                schema: "public",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "released_handle",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    handle = table.Column<string>(type: "text", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reserved_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_released_handle", x => x.id);
                    table.CheckConstraint("ck_released_handle_lowercase", "handle = lower(handle)");
                    table.CheckConstraint("ck_released_handle_period", "reserved_until > released_at");
                    table.ForeignKey(
                        name: "fk_released_handle_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_released_handle_handle",
                schema: "public",
                table: "released_handle",
                column: "handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_released_handle_profile_id",
                schema: "public",
                table: "released_handle",
                column: "profile_id");

            // Handles are unique without regard to case. Profiles that share a handle apart from
            // case keep the handle of the oldest profile; the others get their id as suffix.
            migrationBuilder.Sql("""
                UPDATE public.profile p
                SET username = p.username || '-' || p.id
                WHERE EXISTS (
                    SELECT 1 FROM public.profile o
                    WHERE lower(o.username) = lower(p.username) AND o.id < p.id);
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_profile_username_lower ON public.profile (lower(username));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX public.ux_profile_username_lower;");

            migrationBuilder.DropTable(
                name: "released_handle",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "handle_changed_at",
                schema: "public",
                table: "profile");
        }
    }
}
