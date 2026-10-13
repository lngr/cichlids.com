using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRedeliverySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<long>>(
                name: "acknowledged_subscription_ids",
                schema: "public",
                table: "outbox_event",
                type: "bigint[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "given_up_at",
                schema: "public",
                table: "outbox_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at",
                schema: "public",
                table: "outbox_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                schema: "public",
                table: "outbox_event",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "redelivered_at",
                schema: "public",
                table: "outbox_event",
                type: "timestamp with time zone",
                nullable: true);

            // Open events are due at once. Rows marked dispatched with the error 'gave up' are
            // given-up events and move to given_up_at.
            migrationBuilder.Sql("UPDATE outbox_event SET next_attempt_at = occurred_at WHERE dispatched_at IS NULL");
            migrationBuilder.Sql(
                "UPDATE outbox_event SET given_up_at = dispatched_at, dispatched_at = NULL WHERE last_error = 'gave up'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_due",
                schema: "public",
                table: "outbox_event",
                column: "next_attempt_at",
                filter: "dispatched_at IS NULL AND given_up_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE outbox_event SET dispatched_at = given_up_at, last_error = 'gave up' WHERE given_up_at IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "ix_outbox_event_due",
                schema: "public",
                table: "outbox_event");

            migrationBuilder.DropColumn(
                name: "acknowledged_subscription_ids",
                schema: "public",
                table: "outbox_event");

            migrationBuilder.DropColumn(
                name: "given_up_at",
                schema: "public",
                table: "outbox_event");

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                schema: "public",
                table: "outbox_event");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                schema: "public",
                table: "outbox_event");

            migrationBuilder.DropColumn(
                name: "redelivered_at",
                schema: "public",
                table: "outbox_event");
        }
    }
}
