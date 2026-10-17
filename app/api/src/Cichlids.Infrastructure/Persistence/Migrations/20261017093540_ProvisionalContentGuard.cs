using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProvisionalContentGuard : Migration
    {

        // Tables with columns that hold the profile a row belongs to. The column names are the arguments of the trigger function.
        private static readonly (string Table, string Columns)[] ProfileGuards =
        [
            ("follow", "follower_profile_id, target_profile_id"),
            ("comment", "author_profile_id"),
            ("rating", "profile_id"),
            ("comment_vote", "profile_id"),
            ("tank_selected_member", "member_profile_id"),
            ("story", "author_profile_id"),
            ("discussion_post", "author_profile_id"),
            ("collection", "profile_id"),
            ("media_link", "profile_id"),
        ];

        // Tables with a column that references a tank whose rows reach other people.
        private static readonly (string Table, string Column)[] TankGuards =
        [
            ("follow", "target_tank_id"),
            ("comment", "tank_id"),
            ("rating", "tank_id"),
            ("tank_selected_member", "tank_id"),
            ("story_subject", "tank_id"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Content of a provisional profile stays private whichever statement writes it: the triggers fire for
            // entity saves, set-based updates, raw SQL and cascades alike. A refusal raises SQLSTATE PV001. The kind
            // of the profile is read at the time of the write, so a profile that has become a member and a tank that
            // moves into a member profile write freely. Deletes are never refused.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.profile_is_provisional(p_profile_id bigint) RETURNS boolean LANGUAGE sql STABLE AS $$
                    SELECT EXISTS (SELECT 1 FROM public.profile WHERE id = p_profile_id AND kind = 'provisional')
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.tank_of_provisional_profile_must_be_private() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF public.profile_is_provisional(NEW.profile_id) THEN
                        RAISE EXCEPTION 'The tank % of the provisional profile % must have private profile and stream visibility.', NEW.id, NEW.profile_id
                            USING ERRCODE = 'PV001';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER tank_of_provisional_profile_must_be_private BEFORE INSERT OR UPDATE ON public.tank
                    FOR EACH ROW WHEN (NEW.profile_visibility <> 'private' OR NEW.stream_visibility <> 'private')
                    EXECUTE FUNCTION public.tank_of_provisional_profile_must_be_private();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.reject_provisional_profile_rows() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    column_name text;
                    owner_id bigint;
                BEGIN
                    FOREACH column_name IN ARRAY TG_ARGV LOOP
                        owner_id := (to_jsonb(NEW) ->> column_name)::bigint;
                        IF owner_id IS NOT NULL AND public.profile_is_provisional(owner_id) THEN
                            RAISE EXCEPTION 'A row of % must not belong to the provisional profile % (column %).', TG_TABLE_NAME, owner_id, column_name
                                USING ERRCODE = 'PV001';
                        END IF;
                    END LOOP;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.reject_rows_of_provisional_tanks() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    tank_id bigint;
                BEGIN
                    tank_id := (to_jsonb(NEW) ->> TG_ARGV[0])::bigint;
                    IF tank_id IS NOT NULL AND EXISTS (
                        SELECT 1 FROM public.tank t WHERE t.id = tank_id AND public.profile_is_provisional(t.profile_id)) THEN
                        RAISE EXCEPTION 'A row of % must not reference the tank % of a provisional profile (column %).', TG_TABLE_NAME, tank_id, TG_ARGV[0]
                            USING ERRCODE = 'PV001';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            foreach (var (table, columns) in ProfileGuards)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER provisional_content_guard BEFORE INSERT OR UPDATE OF {columns} ON public.{table}
                        FOR EACH ROW EXECUTE FUNCTION public.reject_provisional_profile_rows({columns});
                    """);
            }

            foreach (var (table, column) in TankGuards)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER provisional_tank_guard BEFORE INSERT OR UPDATE OF {column} ON public.{table}
                        FOR EACH ROW EXECUTE FUNCTION public.reject_rows_of_provisional_tanks({column});
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in TankGuards)
            {
                migrationBuilder.Sql($"DROP TRIGGER provisional_tank_guard ON public.{table};");
            }

            foreach (var (table, _) in ProfileGuards)
            {
                migrationBuilder.Sql($"DROP TRIGGER provisional_content_guard ON public.{table};");
            }

            migrationBuilder.Sql("""
                DROP TRIGGER tank_of_provisional_profile_must_be_private ON public.tank;
                DROP FUNCTION public.reject_rows_of_provisional_tanks();
                DROP FUNCTION public.reject_provisional_profile_rows();
                DROP FUNCTION public.tank_of_provisional_profile_must_be_private();
                DROP FUNCTION public.profile_is_provisional(bigint);
                """);
        }
    }
}
