using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptWebSessionTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored tokens are plain text, which the API reads as undecryptable. Deleting the
            // sessions removes the plain text tokens at once; their members sign in again.
            migrationBuilder.Sql("DELETE FROM public.web_session;");

            migrationBuilder.RenameColumn(
                name: "refresh_token",
                schema: "public",
                table: "web_session",
                newName: "encrypted_refresh_token");

            migrationBuilder.RenameColumn(
                name: "id_token",
                schema: "public",
                table: "web_session",
                newName: "encrypted_id_token");

            migrationBuilder.RenameColumn(
                name: "access_token",
                schema: "public",
                table: "web_session",
                newName: "encrypted_access_token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Encrypted tokens are unusable to a schema that expects plain text.
            migrationBuilder.Sql("DELETE FROM public.web_session;");

            migrationBuilder.RenameColumn(
                name: "encrypted_refresh_token",
                schema: "public",
                table: "web_session",
                newName: "refresh_token");

            migrationBuilder.RenameColumn(
                name: "encrypted_id_token",
                schema: "public",
                table: "web_session",
                newName: "id_token");

            migrationBuilder.RenameColumn(
                name: "encrypted_access_token",
                schema: "public",
                table: "web_session",
                newName: "access_token");
        }
    }
}
