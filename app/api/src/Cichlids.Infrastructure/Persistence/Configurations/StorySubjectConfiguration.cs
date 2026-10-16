using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class StorySubjectConfiguration : IEntityTypeConfiguration<StorySubject>
{
    public void Configure(EntityTypeBuilder<StorySubject> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubjectType).HasConversion<SnakeCaseEnumConverter<StorySubjectType>>().IsRequired();

        builder.HasOne<Story>().WithMany(x => x.Subjects).HasForeignKey(x => x.StoryId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Inhabitant>().WithMany().HasForeignKey(x => x.InhabitantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Species>().WithMany().HasForeignKey(x => x.SpeciesId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.StoryId, x.TankId }).IsUnique().HasFilter("tank_id IS NOT NULL")
            .HasDatabaseName("ux_story_subject_story_tank");
        builder.HasIndex(x => new { x.StoryId, x.InhabitantId }).IsUnique().HasFilter("inhabitant_id IS NOT NULL")
            .HasDatabaseName("ux_story_subject_story_inhabitant");
        builder.HasIndex(x => new { x.StoryId, x.SpeciesId }).IsUnique().HasFilter("species_id IS NOT NULL")
            .HasDatabaseName("ux_story_subject_story_species");
        builder.HasIndex(x => new { x.StoryId, x.Position }).IsUnique().HasFilter("position IS NOT NULL")
            .HasDatabaseName("ux_story_subject_story_position");
        builder.HasIndex(x => x.TankId).HasFilter("tank_id IS NOT NULL");
        builder.HasIndex(x => x.InhabitantId).HasFilter("inhabitant_id IS NOT NULL");
        builder.HasIndex(x => x.SpeciesId).HasFilter("species_id IS NOT NULL");

        builder.ToTable("story_subject", table =>
        {
            table.HasCheckConstraint("ck_story_subject_type", "subject_type IN ('tank', 'inhabitant', 'species')");
            table.HasCheckConstraint("ck_story_subject_key", """
                num_nonnulls(tank_id, inhabitant_id, species_id) = 1 AND (
                    (subject_type = 'tank' AND tank_id IS NOT NULL)
                    OR (subject_type = 'inhabitant' AND inhabitant_id IS NOT NULL)
                    OR (subject_type = 'species' AND species_id IS NOT NULL))
                """);
            table.HasCheckConstraint("ck_story_subject_position", "position IS NULL OR position >= 0");
        });
    }
}
