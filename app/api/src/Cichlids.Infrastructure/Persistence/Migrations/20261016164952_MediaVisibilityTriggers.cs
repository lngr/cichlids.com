using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaVisibilityTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every change that can alter who sees a media item changes its visibility generation in the same
            // transaction, whichever statement makes the change: the triggers fire for entity saves, set-based
            // updates, raw SQL and cascades alike.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.media_item_change_visibility(item_ids bigint[]) RETURNS void LANGUAGE sql AS $$
                    UPDATE public.media_item SET visibility_generation = visibility_generation + 1,
                        placement_pending = true, placement_attempts = 0, placement_next_attempt_at = NULL
                    WHERE id IN (SELECT id FROM public.media_item WHERE id = ANY(item_ids) ORDER BY id FOR UPDATE)
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.media_item_ids_of_tank(p_tank_id bigint) RETURNS bigint[] LANGUAGE sql STABLE AS $$
                    SELECT COALESCE(array_agg(DISTINCT media_item_id), '{}') FROM (
                        SELECT media_item_id FROM public.media_link WHERE tank_id = p_tank_id
                        UNION ALL SELECT l.media_item_id FROM public.media_link l JOIN public.care_entry e ON e.id = l.care_entry_id WHERE e.tank_id = p_tank_id
                        UNION ALL SELECT l.media_item_id FROM public.media_link l JOIN public.water_change_session s ON s.id = l.water_change_session_id WHERE s.tank_id = p_tank_id
                        UNION ALL SELECT l.media_item_id FROM public.media_link l JOIN public.inhabitant i ON i.id = l.inhabitant_id WHERE i.tank_id = p_tank_id
                    ) items
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.media_link_changes_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY[COALESCE(NEW.media_item_id, OLD.media_item_id)]);
                    RETURN NULL;
                END $$;
                CREATE TRIGGER media_link_changes_visibility AFTER INSERT OR DELETE ON public.media_link
                    FOR EACH ROW EXECUTE FUNCTION public.media_link_changes_visibility();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.tank_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(public.media_item_ids_of_tank(NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER tank_changes_media_visibility AFTER UPDATE ON public.tank
                    FOR EACH ROW WHEN (OLD.profile_visibility IS DISTINCT FROM NEW.profile_visibility
                        OR OLD.stream_visibility IS DISTINCT FROM NEW.stream_visibility
                        OR OLD.state IS DISTINCT FROM NEW.state OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at)
                    EXECUTE FUNCTION public.tank_changes_media_visibility();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.care_entry_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE entry public.care_entry;
                BEGIN
                    entry := CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                    PERFORM public.media_item_change_visibility(ARRAY(
                        SELECT media_item_id FROM public.media_link
                        WHERE care_entry_id = entry.id
                            OR (entry.water_change_session_id IS NOT NULL AND water_change_session_id = entry.water_change_session_id)));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER care_entry_changes_media_visibility AFTER INSERT OR DELETE ON public.care_entry
                    FOR EACH ROW EXECUTE FUNCTION public.care_entry_changes_media_visibility();
                CREATE TRIGGER care_entry_updates_media_visibility AFTER UPDATE ON public.care_entry
                    FOR EACH ROW WHEN (OLD.only_me IS DISTINCT FROM NEW.only_me
                        OR OLD.water_change_session_id IS DISTINCT FROM NEW.water_change_session_id OR OLD.tank_id IS DISTINCT FROM NEW.tank_id)
                    EXECUTE FUNCTION public.care_entry_changes_media_visibility();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.post_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY(SELECT media_item_id FROM public.media_link WHERE post_id = NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER post_changes_media_visibility AFTER UPDATE ON public.post
                    FOR EACH ROW WHEN (OLD.state IS DISTINCT FROM NEW.state OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at
                        OR OLD.author_profile_id IS DISTINCT FROM NEW.author_profile_id)
                    EXECUTE FUNCTION public.post_changes_media_visibility();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.profile_changes_media_visibility() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM public.media_item_change_visibility(ARRAY(SELECT media_item_id FROM public.media_link WHERE profile_id = NEW.id));
                    RETURN NULL;
                END $$;
                CREATE TRIGGER profile_changes_media_visibility AFTER UPDATE ON public.profile
                    FOR EACH ROW WHEN (OLD.suspended_until IS DISTINCT FROM NEW.suspended_until)
                    EXECUTE FUNCTION public.profile_changes_media_visibility();
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER profile_changes_media_visibility ON public.profile;
                DROP TRIGGER post_changes_media_visibility ON public.post;
                DROP TRIGGER care_entry_updates_media_visibility ON public.care_entry;
                DROP TRIGGER care_entry_changes_media_visibility ON public.care_entry;
                DROP TRIGGER tank_changes_media_visibility ON public.tank;
                DROP TRIGGER media_link_changes_visibility ON public.media_link;
                DROP FUNCTION public.profile_changes_media_visibility();
                DROP FUNCTION public.post_changes_media_visibility();
                DROP FUNCTION public.care_entry_changes_media_visibility();
                DROP FUNCTION public.tank_changes_media_visibility();
                DROP FUNCTION public.media_link_changes_visibility();
                DROP FUNCTION public.media_item_ids_of_tank(bigint);
                DROP FUNCTION public.media_item_change_visibility(bigint[]);
                """);


        }
    }
}
