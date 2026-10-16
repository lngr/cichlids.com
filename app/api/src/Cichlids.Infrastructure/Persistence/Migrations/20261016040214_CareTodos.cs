using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareTodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_todo",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    care_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_todo", x => x.id);
                    table.CheckConstraint("ck_care_todo_text", "char_length(text) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "fk_care_todo_care_entry_care_entry_id",
                        column: x => x.care_entry_id,
                        principalSchema: "public",
                        principalTable: "care_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_care_todo_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_care_todo_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_todo_care_entry_id",
                schema: "public",
                table: "care_todo",
                column: "care_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_todo_profile_done",
                schema: "public",
                table: "care_todo",
                columns: new[] { "profile_id", "done_at", "id" },
                descending: new[] { false, true, true },
                filter: "done_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_care_todo_profile_open",
                schema: "public",
                table: "care_todo",
                columns: new[] { "profile_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "done_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_care_todo_tank_id",
                schema: "public",
                table: "care_todo",
                column: "tank_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_todo",
                schema: "public");
        }
    }
}
