using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CareReminderSnoozes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_reminder_snooze",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    until_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_reminder_snooze", x => x.id);
                    table.CheckConstraint("ck_care_reminder_snooze_kind", "kind IN ('water_change', 'measurement')");
                    table.ForeignKey(
                        name: "fk_care_reminder_snooze_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_care_reminder_snooze_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_reminder_snooze_profile_id_tank_id_kind_due_date",
                schema: "public",
                table: "care_reminder_snooze",
                columns: new[] { "profile_id", "tank_id", "kind", "due_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_care_reminder_snooze_tank_id",
                schema: "public",
                table: "care_reminder_snooze",
                column: "tank_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_reminder_snooze",
                schema: "public");
        }
    }
}
