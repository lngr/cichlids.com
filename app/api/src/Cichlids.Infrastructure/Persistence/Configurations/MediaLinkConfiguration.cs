using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class MediaLinkConfiguration : IEntityTypeConfiguration<MediaLink>
{
    private const string ContextKeys =
        "num_nonnulls(tank_id, inhabitant_id, care_entry_id, water_change_session_id, story_id, discussion_post_id, profile_id) = 1";

    public void Configure(EntityTypeBuilder<MediaLink> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContextType).HasConversion<SnakeCaseEnumConverter<MediaLinkContext>>().IsRequired();
        builder.Property(x => x.Role).HasConversion<SnakeCaseEnumConverter<MediaLinkRole>>().IsRequired();
        builder.Property(x => x.Position).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany(x => x.MediaLinks).HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Inhabitant>().WithMany().HasForeignKey(x => x.InhabitantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CareEntry>().WithMany(x => x.MediaLinks).HasForeignKey(x => x.CareEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WaterChangeSession>().WithMany(x => x.MediaLinks).HasForeignKey(x => x.WaterChangeSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Story>().WithMany(x => x.MediaLinks).HasForeignKey(x => x.StoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DiscussionPost>().WithMany(x => x.MediaLinks).HasForeignKey(x => x.DiscussionPostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MediaItemId);
        builder.HasIndex(x => new { x.TankId, x.Role, x.MediaItemId }).IsUnique().HasFilter("tank_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_tank_role_item");
        builder.HasIndex(x => x.TankId).IsUnique().HasFilter("role = 'main' AND tank_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_tank_main");
        builder.HasIndex(x => new { x.InhabitantId, x.Role, x.MediaItemId }).IsUnique().HasFilter("inhabitant_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_inhabitant_role_item");
        builder.HasIndex(x => new { x.CareEntryId, x.Role, x.MediaItemId }).IsUnique().HasFilter("care_entry_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_care_entry_role_item");
        builder.HasIndex(x => new { x.CareEntryId, x.Role, x.Position }).IsUnique().HasFilter("care_entry_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_care_entry_role_position");
        builder.HasIndex(x => new { x.WaterChangeSessionId, x.Role, x.MediaItemId }).IsUnique().HasFilter("water_change_session_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_session_role_item");
        builder.HasIndex(x => new { x.WaterChangeSessionId, x.Role, x.Position }).IsUnique().HasFilter("water_change_session_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_session_role_position");
        builder.HasIndex(x => new { x.StoryId, x.Role, x.MediaItemId }).IsUnique().HasFilter("story_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_story_role_item");
        builder.HasIndex(x => new { x.StoryId, x.Position }).IsUnique().HasFilter("story_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_story_position");
        builder.HasIndex(x => new { x.DiscussionPostId, x.Role, x.MediaItemId }).IsUnique().HasFilter("discussion_post_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_discussion_post_role_item");
        builder.HasIndex(x => new { x.ProfileId, x.Role }).IsUnique().HasFilter("profile_id IS NOT NULL")
            .HasDatabaseName("ux_media_link_profile_role");

        builder.ToTable("media_link", table =>
        {
            table.HasCheckConstraint("ck_media_link_context_type",
                "context_type IN ('tank', 'inhabitant', 'care_entry', 'water_change_session', 'story', 'discussion_post', 'profile')");
            table.HasCheckConstraint("ck_media_link_context_key", $$"""
                {{ContextKeys}} AND (
                    (context_type = 'tank' AND tank_id IS NOT NULL)
                    OR (context_type = 'inhabitant' AND inhabitant_id IS NOT NULL)
                    OR (context_type = 'care_entry' AND care_entry_id IS NOT NULL)
                    OR (context_type = 'water_change_session' AND water_change_session_id IS NOT NULL)
                    OR (context_type = 'story' AND story_id IS NOT NULL)
                    OR (context_type = 'discussion_post' AND discussion_post_id IS NOT NULL)
                    OR (context_type = 'profile' AND profile_id IS NOT NULL))
                """);
            table.HasCheckConstraint("ck_media_link_role", """
                (context_type = 'tank' AND role IN ('main', 'showcase', 'decoration', 'technic', 'time_lapse', 'video', 'decommission', 'recommission'))
                OR (context_type = 'care_entry' AND role IN ('photo', 'video'))
                OR (context_type = 'water_change_session' AND role = 'evidence')
                OR (context_type = 'story' AND role = 'item')
                OR (context_type = 'discussion_post' AND role = 'attachment')
                OR (context_type = 'profile' AND role IN ('avatar', 'header'))
                """);
            table.HasCheckConstraint("ck_media_link_position", $$"""
                position >= 0
                AND (NOT (context_type = 'care_entry' AND role = 'photo') OR position < {{MediaLinkLimits.CarePhotosPerEntry}})
                AND (context_type <> 'water_change_session' OR position < {{MediaLinkLimits.EvidencePhotosPerSession}})
                """);
        });
    }
}
