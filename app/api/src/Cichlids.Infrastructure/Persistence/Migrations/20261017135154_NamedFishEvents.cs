using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NamedFishEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link");

            migrationBuilder.CreateTable(
                name: "inhabitant_event",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    inhabitant_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(7,3)", nullable: true),
                    length_input_value = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    length_input_unit = table.Column<string>(type: "text", nullable: true),
                    offspring_generation = table.Column<int>(type: "integer", nullable: true),
                    offspring_hybrid = table.Column<bool>(type: "boolean", nullable: true),
                    from_tank_id = table.Column<long>(type: "bigint", nullable: true),
                    to_tank_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inhabitant_event", x => x.id);
                    table.CheckConstraint("ck_inhabitant_event_growth", "(kind = 'growth') = (length_cm IS NOT NULL AND length_input_value IS NOT NULL AND length_input_unit IS NOT NULL)\nAND (length_cm IS NULL OR (length_cm > 0 AND length_cm <= 300))\nAND (length_input_unit IS NULL OR length_input_unit IN ('cm', 'inch'))\nAND (length_input_value IS NULL OR length_input_value > 0)");
                    table.CheckConstraint("ck_inhabitant_event_kind", "kind IN ('growth', 'spawning', 'moved', 'died')");
                    table.CheckConstraint("ck_inhabitant_event_move", "(kind = 'moved') = (from_tank_id IS NOT NULL AND to_tank_id IS NOT NULL) AND (from_tank_id IS NULL OR from_tank_id <> to_tank_id)");
                    table.CheckConstraint("ck_inhabitant_event_spawning", "(kind = 'spawning') = (offspring_hybrid IS NOT NULL)\nAND (kind <> 'spawning' OR (offspring_hybrid AND offspring_generation IS NULL)\n    OR (NOT offspring_hybrid AND offspring_generation BETWEEN 1 AND 99))\nAND (kind = 'spawning' OR offspring_generation IS NULL)");
                    table.ForeignKey(
                        name: "fk_inhabitant_event_inhabitant_inhabitant_id",
                        column: x => x.inhabitant_id,
                        principalSchema: "public",
                        principalTable: "inhabitant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inhabitant_event_tanks_from_tank_id",
                        column: x => x.from_tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inhabitant_event_tanks_to_tank_id",
                        column: x => x.to_tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link",
                sql: "(context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))\nOR (context_type = 'inhabitant' AND role IN ('photo', 'video'))\nOR (context_type = 'care_entry' AND role IN ('photo', 'video'))\nOR (context_type = 'water_change_session' AND role = 'evidence')\nOR (context_type = 'story' AND role = 'item')\nOR (context_type = 'discussion_post' AND role = 'attachment')\nOR (context_type = 'profile' AND role IN ('avatar', 'header'))");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_event_from_tank_id",
                schema: "public",
                table: "inhabitant_event",
                column: "from_tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_event_inhabitant_id_occurred_on",
                schema: "public",
                table: "inhabitant_event",
                columns: new[] { "inhabitant_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_event_to_tank_id",
                schema: "public",
                table: "inhabitant_event",
                column: "to_tank_id");

            migrationBuilder.CreateIndex(
                name: "ux_inhabitant_event_died",
                schema: "public",
                table: "inhabitant_event",
                column: "inhabitant_id",
                unique: true,
                filter: "kind = 'died'");

            // A named fish that died has its death on the timeline.
            migrationBuilder.Sql("""
                INSERT INTO public.inhabitant_event (inhabitant_id, kind, occurred_on)
                SELECT id, 'died', removed_on FROM public.inhabitant WHERE name IS NOT NULL AND removal_reason = 'died';
                """);

            // The media of a fish follow the visibility of its tank, so a move to another tank changes who sees
            // them. The visibility generation changes in the same transaction whichever statement moves the fish.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.inhabitant_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY(SELECT media_item_id FROM public.media_link WHERE inhabitant_id = NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER inhabitant_changes_media_visibility AFTER UPDATE OF tank_id ON public.inhabitant
                    FOR EACH ROW WHEN (OLD.tank_id IS DISTINCT FROM NEW.tank_id)
                    EXECUTE FUNCTION public.inhabitant_changes_media_visibility();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER inhabitant_changes_media_visibility ON public.inhabitant;
                DROP FUNCTION public.inhabitant_changes_media_visibility();
                """);

            migrationBuilder.DropTable(
                name: "inhabitant_event",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link");

            migrationBuilder.Sql("DELETE FROM public.media_link WHERE context_type = 'inhabitant';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_link_role",
                schema: "public",
                table: "media_link",
                sql: "(context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))\nOR (context_type = 'care_entry' AND role IN ('photo', 'video'))\nOR (context_type = 'water_change_session' AND role = 'evidence')\nOR (context_type = 'story' AND role = 'item')\nOR (context_type = 'discussion_post' AND role = 'attachment')\nOR (context_type = 'profile' AND role IN ('avatar', 'header'))");
        }
    }
}
