using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileArchiveRetrospective : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archive_retrospective_seen_at",
                schema: "public",
                table: "profile",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_retrospective_seen_at",
                schema: "public",
                table: "profile");
        }
    }
}
