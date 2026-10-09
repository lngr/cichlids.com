using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cichlids.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.EnsureSchema(
                name: "archive");

            migrationBuilder.CreateTable(
                name: "legacy_comment",
                schema: "archive",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: false),
                    legacy_target_type = table.Column<string>(type: "text", nullable: false),
                    legacy_target_id = table.Column<int>(type: "integer", nullable: false),
                    author_legacy_user_id = table.Column<int>(type: "integer", nullable: true),
                    poster_name = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    stars = table.Column<short>(type: "smallint", nullable: true),
                    created_at_legacy = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    vault_reason = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_comment", x => x.id);
                    table.CheckConstraint("ck_legacy_comment_target_type", "legacy_target_type IN ('picture', 'tank')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_event",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_type = table.Column<string>(type: "text", nullable: false),
                    aggregate_id = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "species",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    genus = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    temperature_range = table.Column<string>(type: "text", nullable: true),
                    ph_range = table.Column<string>(type: "text", nullable: true),
                    gh_range = table.Column<string>(type: "text", nullable: true),
                    kh_range = table.Column<string>(type: "text", nullable: true),
                    max_size = table.Column<string>(type: "text", nullable: true),
                    breeding = table.Column<string>(type: "text", nullable: false),
                    aggression = table.Column<string>(type: "text", nullable: false),
                    intra_aggression = table.Column<string>(type: "text", nullable: false),
                    diet = table.Column<string>(type: "text", nullable: false),
                    morphs = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: true),
                    habitat = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species", x => x.id);
                    table.CheckConstraint("ck_species_aggression", "aggression IN ('unspecified', 'low', 'moderate', 'high')");
                    table.CheckConstraint("ck_species_breeding", "breeding IN ('unspecified', 'mouthbreeder', 'cave_breeder', 'substrate_breeder')");
                    table.CheckConstraint("ck_species_diet", "diet IN ('unspecified', 'omnivore', 'carnivore', 'herbivore', 'limnivore')");
                    table.CheckConstraint("ck_species_intra_aggression", "intra_aggression IN ('unspecified', 'low', 'moderate', 'high')");
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscription",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    url = table.Column<string>(type: "text", nullable: false),
                    secret = table.Column<string>(type: "text", nullable: false),
                    event_types = table.Column<List<string>>(type: "text[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_subscription", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "species_common_name",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    species_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species_common_name", x => x.id);
                    table.ForeignKey(
                        name: "fk_species_common_name_species_species_id",
                        column: x => x.species_id,
                        principalSchema: "public",
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "species_link",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    species_id = table.Column<long>(type: "bigint", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_species_link", x => x.id);
                    table.ForeignKey(
                        name: "fk_species_link_species_species_id",
                        column: x => x.species_id,
                        principalSchema: "public",
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collection_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    collection_id = table.Column<long>(type: "bigint", nullable: false),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_entry_collection_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "public",
                        principalTable: "collection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comment",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    post_id = table.Column<long>(type: "bigint", nullable: true),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    poster_name = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delete_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_by_profile_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment", x => x.id);
                    table.CheckConstraint("ck_comment_body_not_empty", "body <> ''");
                    table.CheckConstraint("ck_comment_exactly_one_target", "(post_id IS NULL) <> (tank_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "comment_vote",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    comment_id = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_vote", x => x.id);
                    table.CheckConstraint("ck_comment_vote_value", "value IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_comment_vote_comment_comment_id",
                        column: x => x.comment_id,
                        principalSchema: "public",
                        principalTable: "comment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follow",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    follower_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    target_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    target_tank_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow", x => x.id);
                    table.CheckConstraint("ck_follow_exactly_one_target", "(target_profile_id IS NULL) <> (target_tank_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "inhabitant",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    species_id = table.Column<long>(type: "bigint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inhabitant", x => x.id);
                    table.ForeignKey(
                        name: "fk_inhabitant_species_species_id",
                        column: x => x.species_id,
                        principalSchema: "public",
                        principalTable: "species",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_item",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    owner_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    original_filename = table.Column<string>(type: "text", nullable: true),
                    content_type = table.Column<string>(type: "text", nullable: true),
                    byte_size = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    checksum_sha256 = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_item", x => x.id);
                    table.CheckConstraint("ck_media_item_kind", "kind IN ('photo', 'video')");
                });

            migrationBuilder.CreateTable(
                name: "media_variant",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_variant", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_variant_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    username = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    country_code = table.Column<string>(type: "text", nullable: true),
                    external_avatar_url = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    profile_image_media_id = table.Column<long>(type: "bigint", nullable: true),
                    avatar_media_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile", x => x.id);
                    table.CheckConstraint("ck_profile_kind", "kind IN ('member', 'archived', 'system')");
                    table.ForeignKey(
                        name: "fk_profile_media_item_avatar_media_id",
                        column: x => x.avatar_media_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_profile_media_item_profile_image_media_id",
                        column: x => x.profile_image_media_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profile_identity",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_identity", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_identity_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tank",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "text", nullable: true),
                    width_value = table.Column<int>(type: "integer", nullable: true),
                    height_value = table.Column<int>(type: "integer", nullable: true),
                    depth_value = table.Column<int>(type: "integer", nullable: true),
                    dimension_unit = table.Column<string>(type: "text", nullable: true),
                    gravel = table.Column<string>(type: "text", nullable: true),
                    plants = table.Column<string>(type: "text", nullable: true),
                    decoration = table.Column<string>(type: "text", nullable: true),
                    light = table.Column<string>(type: "text", nullable: true),
                    light_duration = table.Column<string>(type: "text", nullable: true),
                    filtration = table.Column<string>(type: "text", nullable: true),
                    technic = table.Column<string>(type: "text", nullable: true),
                    water_ph = table.Column<string>(type: "text", nullable: true),
                    water_kh = table.Column<string>(type: "text", nullable: true),
                    water_gh = table.Column<string>(type: "text", nullable: true),
                    water_no2 = table.Column<string>(type: "text", nullable: true),
                    water_no3 = table.Column<string>(type: "text", nullable: true),
                    water_po4 = table.Column<string>(type: "text", nullable: true),
                    water_notes = table.Column<string>(type: "text", nullable: true),
                    food = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    main_media_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delete_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_by_profile_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank", x => x.id);
                    table.CheckConstraint("ck_tank_category", "category IS NULL OR category IN ('tanganyika', 'malawi', 'american', 'african', 'community', 'central_american', 'south_american')");
                    table.CheckConstraint("ck_tank_dimension_unit", "dimension_unit IS NULL OR dimension_unit IN ('cm', 'inch')");
                    table.CheckConstraint("ck_tank_state", "state IN ('draft', 'published')");
                    table.ForeignKey(
                        name: "fk_tank_media_item_main_media_id",
                        column: x => x.main_media_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tank_profile_deleted_by_profile_id",
                        column: x => x.deleted_by_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tank_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_id = table.Column<int>(type: "integer", nullable: true),
                    author_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    view_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    rating_average = table.Column<double>(type: "double precision", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delete_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_by_profile_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post", x => x.id);
                    table.CheckConstraint("ck_post_kind", "kind IN ('single', 'story')");
                    table.CheckConstraint("ck_post_state", "state IN ('draft', 'published', 'archived')");
                    table.CheckConstraint("ck_post_topic", "topic IN ('cichlids', 'tanks', 'offtopic', 'contest', 'unknown')");
                    table.ForeignKey(
                        name: "fk_post_profiles_author_profile_id",
                        column: x => x.author_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_post_profiles_deleted_by_profile_id",
                        column: x => x.deleted_by_profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_post_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tank_media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tank_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tank_media", x => x.id);
                    table.CheckConstraint("ck_tank_media_section", "section IN ('showcase', 'decoration', 'technic')");
                    table.ForeignKey(
                        name: "fk_tank_media_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tank_media_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    media_item_id = table.Column<long>(type: "bigint", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_media_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalSchema: "public",
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_media_posts_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rating",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    legacy_comment_id = table.Column<int>(type: "integer", nullable: true),
                    post_id = table.Column<long>(type: "bigint", nullable: true),
                    tank_id = table.Column<long>(type: "bigint", nullable: true),
                    profile_id = table.Column<long>(type: "bigint", nullable: true),
                    stars = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating", x => x.id);
                    table.CheckConstraint("ck_rating_exactly_one_target", "(post_id IS NULL) <> (tank_id IS NULL)");
                    table.CheckConstraint("ck_rating_stars_range", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_rating_post_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rating_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rating_tanks_tank_id",
                        column: x => x.tank_id,
                        principalSchema: "public",
                        principalTable: "tank",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "slug_alias",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    post_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    is_canonical = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slug_alias", x => x.id);
                    table.ForeignKey(
                        name: "fk_slug_alias_post_post_id",
                        column: x => x.post_id,
                        principalSchema: "public",
                        principalTable: "post",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_legacy_id",
                schema: "public",
                table: "collection",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_profile_id",
                schema: "public",
                table: "collection",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_entry_collection_id_post_id",
                schema: "public",
                table: "collection_entry",
                columns: new[] { "collection_id", "post_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_entry_post_id",
                schema: "public",
                table: "collection_entry",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_author_profile_id",
                schema: "public",
                table: "comment",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_deleted_by_profile_id",
                schema: "public",
                table: "comment",
                column: "deleted_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_legacy_id",
                schema: "public",
                table: "comment",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comment_post_id",
                schema: "public",
                table: "comment",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_tank_id",
                schema: "public",
                table: "comment",
                column: "tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_comment_vote_comment_id_profile_id",
                schema: "public",
                table: "comment_vote",
                columns: new[] { "comment_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comment_vote_profile_id",
                schema: "public",
                table: "comment_vote",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_follower_profile_id_target_profile_id",
                schema: "public",
                table: "follow",
                columns: new[] { "follower_profile_id", "target_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_follow_follower_profile_id_target_tank_id",
                schema: "public",
                table: "follow",
                columns: new[] { "follower_profile_id", "target_tank_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_follow_target_profile_id",
                schema: "public",
                table: "follow",
                column: "target_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_target_tank_id",
                schema: "public",
                table: "follow",
                column: "target_tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_species_id",
                schema: "public",
                table: "inhabitant",
                column: "species_id");

            migrationBuilder.CreateIndex(
                name: "ix_inhabitant_tank_id",
                schema: "public",
                table: "inhabitant",
                column: "tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_legacy_comment_legacy_id",
                schema: "archive",
                table: "legacy_comment",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_item_legacy_id",
                schema: "public",
                table: "media_item",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_item_owner_profile_id",
                schema: "public",
                table: "media_item",
                column: "owner_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_item_storage_key",
                schema: "public",
                table: "media_item",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_variant_media_item_id_label",
                schema: "public",
                table: "media_variant",
                columns: new[] { "media_item_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_variant_storage_key",
                schema: "public",
                table: "media_variant",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_author_profile_id",
                schema: "public",
                table: "post",
                column: "author_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_deleted_by_profile_id",
                schema: "public",
                table: "post",
                column: "deleted_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_legacy_id",
                schema: "public",
                table: "post",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_tank_id",
                schema: "public",
                table: "post",
                column: "tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_media_media_item_id",
                schema: "public",
                table: "post_media",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_media_post_id_media_item_id",
                schema: "public",
                table: "post_media",
                columns: new[] { "post_id", "media_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_avatar_media_id",
                schema: "public",
                table: "profile",
                column: "avatar_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_legacy_id",
                schema: "public",
                table: "profile",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_profile_image_media_id",
                schema: "public",
                table: "profile",
                column: "profile_image_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_username",
                schema: "public",
                table: "profile",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_identity_profile_id",
                schema: "public",
                table: "profile_identity",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_identity_provider_subject",
                schema: "public",
                table: "profile_identity",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rating_legacy_comment_id",
                schema: "public",
                table: "rating",
                column: "legacy_comment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rating_post_id",
                schema: "public",
                table: "rating",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_rating_profile_id",
                schema: "public",
                table: "rating",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_rating_tank_id",
                schema: "public",
                table: "rating",
                column: "tank_id");

            migrationBuilder.CreateIndex(
                name: "ix_slug_alias_post_id",
                schema: "public",
                table: "slug_alias",
                column: "post_id",
                unique: true,
                filter: "is_canonical = true");

            migrationBuilder.CreateIndex(
                name: "ix_slug_alias_value",
                schema: "public",
                table: "slug_alias",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_species_genus_name",
                schema: "public",
                table: "species",
                columns: new[] { "genus", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_species_legacy_id",
                schema: "public",
                table: "species",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_species_common_name_species_id_name",
                schema: "public",
                table: "species_common_name",
                columns: new[] { "species_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_species_link_species_id",
                schema: "public",
                table: "species_link",
                column: "species_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_deleted_by_profile_id",
                schema: "public",
                table: "tank",
                column: "deleted_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_legacy_id",
                schema: "public",
                table: "tank",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tank_main_media_id",
                schema: "public",
                table: "tank",
                column: "main_media_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_profile_id",
                schema: "public",
                table: "tank",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_media_item_id",
                schema: "public",
                table: "tank_media",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_tank_media_tank_id_media_item_id_section",
                schema: "public",
                table: "tank_media",
                columns: new[] { "tank_id", "media_item_id", "section" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_collection_profiles_profile_id",
                schema: "public",
                table: "collection",
                column: "profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_collection_entry_posts_post_id",
                schema: "public",
                table: "collection_entry",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_posts_post_id",
                schema: "public",
                table: "comment",
                column: "post_id",
                principalSchema: "public",
                principalTable: "post",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_profiles_author_profile_id",
                schema: "public",
                table: "comment",
                column: "author_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_profiles_deleted_by_profile_id",
                schema: "public",
                table: "comment",
                column: "deleted_by_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_tanks_tank_id",
                schema: "public",
                table: "comment",
                column: "tank_id",
                principalSchema: "public",
                principalTable: "tank",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comment_vote_profiles_profile_id",
                schema: "public",
                table: "comment_vote",
                column: "profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_follow_profiles_follower_profile_id",
                schema: "public",
                table: "follow",
                column: "follower_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_follow_profiles_target_profile_id",
                schema: "public",
                table: "follow",
                column: "target_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_follow_tanks_target_tank_id",
                schema: "public",
                table: "follow",
                column: "target_tank_id",
                principalSchema: "public",
                principalTable: "tank",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inhabitant_tanks_tank_id",
                schema: "public",
                table: "inhabitant",
                column: "tank_id",
                principalSchema: "public",
                principalTable: "tank",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_media_item_profiles_owner_profile_id",
                schema: "public",
                table: "media_item",
                column: "owner_profile_id",
                principalSchema: "public",
                principalTable: "profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_media_item_profiles_owner_profile_id",
                schema: "public",
                table: "media_item");

            migrationBuilder.DropTable(
                name: "collection_entry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "comment_vote",
                schema: "public");

            migrationBuilder.DropTable(
                name: "follow",
                schema: "public");

            migrationBuilder.DropTable(
                name: "inhabitant",
                schema: "public");

            migrationBuilder.DropTable(
                name: "legacy_comment",
                schema: "archive");

            migrationBuilder.DropTable(
                name: "media_variant",
                schema: "public");

            migrationBuilder.DropTable(
                name: "outbox_event",
                schema: "public");

            migrationBuilder.DropTable(
                name: "post_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "profile_identity",
                schema: "public");

            migrationBuilder.DropTable(
                name: "rating",
                schema: "public");

            migrationBuilder.DropTable(
                name: "slug_alias",
                schema: "public");

            migrationBuilder.DropTable(
                name: "species_common_name",
                schema: "public");

            migrationBuilder.DropTable(
                name: "species_link",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tank_media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "webhook_subscription",
                schema: "public");

            migrationBuilder.DropTable(
                name: "collection",
                schema: "public");

            migrationBuilder.DropTable(
                name: "comment",
                schema: "public");

            migrationBuilder.DropTable(
                name: "species",
                schema: "public");

            migrationBuilder.DropTable(
                name: "post",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tank",
                schema: "public");

            migrationBuilder.DropTable(
                name: "profile",
                schema: "public");

            migrationBuilder.DropTable(
                name: "media_item",
                schema: "public");
        }
    }
}
