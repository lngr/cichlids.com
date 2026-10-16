using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_profile_kind",
                schema: "public",
                table: "profile");

            migrationBuilder.CreateTable(
                name: "device_challenge",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    challenge_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_challenge", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_credential",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    algorithm = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_signed_request_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_credential", x => x.id);
                    table.CheckConstraint("ck_device_credential_algorithm", "algorithm IN ('ES256')");
                    table.CheckConstraint("ck_device_credential_state", "state IN ('active', 'retired')");
                    table.ForeignKey(
                        name: "fk_device_credential_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_profile_kind",
                schema: "public",
                table: "profile",
                sql: "kind IN ('member', 'archived', 'system', 'provisional')");

            migrationBuilder.CreateIndex(
                name: "ix_device_challenge_challenge_hash",
                schema: "public",
                table: "device_challenge",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_challenge_expires_at",
                schema: "public",
                table: "device_challenge",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_last_signed_request_at",
                schema: "public",
                table: "device_credential",
                column: "last_signed_request_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_profile_id",
                schema: "public",
                table: "device_credential",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_credential_public_key",
                schema: "public",
                table: "device_credential",
                column: "public_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_challenge",
                schema: "public");

            migrationBuilder.DropTable(
                name: "device_credential",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_profile_kind",
                schema: "public",
                table: "profile");

            migrationBuilder.AddCheckConstraint(
                name: "ck_profile_kind",
                schema: "public",
                table: "profile",
                sql: "kind IN ('member', 'archived', 'system')");
        }
    }
}
