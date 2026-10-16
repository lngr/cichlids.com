using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebPushRemindersSent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_push_reminder_sent",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<long>(type: "bigint", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_push_reminder_sent", x => x.id);
                    table.CheckConstraint("ck_web_push_reminder_sent_kind", "kind IN ('water_change', 'measurement')");
                    table.ForeignKey(
                        name: "fk_web_push_reminder_sent_tank_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_web_push_reminder_sent_web_push_subscriptions_subscription_",
                        column: x => x.subscription_id,
                        principalSchema: "public",
                        principalTable: "web_push_subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_web_push_reminder_sent_fire_date",
                schema: "public",
                table: "web_push_reminder_sent",
                column: "fire_date");

            migrationBuilder.CreateIndex(
                name: "ix_web_push_reminder_sent_subscription_id_tank_id_kind_due_dat",
                schema: "public",
                table: "web_push_reminder_sent",
                columns: new[] { "subscription_id", "tank_id", "kind", "due_date", "fire_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_web_push_reminder_sent_tank_id",
                schema: "public",
                table: "web_push_reminder_sent",
                column: "tank_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_push_reminder_sent",
                schema: "public");
        }
    }
}
