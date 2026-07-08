using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class SpeciesConfiguration : IEntityTypeConfiguration<Species>
{
    public void Configure(EntityTypeBuilder<Species> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Genus).IsRequired();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.DisplayName).IsRequired();
        builder.Property(x => x.Breeding).HasConversion<SnakeCaseEnumConverter<SpeciesBreeding>>().IsRequired();
        builder.Property(x => x.Aggression).HasConversion<SnakeCaseEnumConverter<AggressionLevel>>().IsRequired();
        builder.Property(x => x.IntraAggression).HasConversion<SnakeCaseEnumConverter<AggressionLevel>>().IsRequired();
        builder.Property(x => x.Diet).HasConversion<SnakeCaseEnumConverter<SpeciesDiet>>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();
        builder.HasIndex(x => new { x.Genus, x.Name }).IsUnique();

        builder.ToTable("species", tb =>
        {
            tb.HasCheckConstraint(
                "ck_species_breeding",
                "breeding IN ('unspecified', 'mouthbreeder', 'cave_breeder', 'substrate_breeder')");
            tb.HasCheckConstraint(
                "ck_species_aggression",
                "aggression IN ('unspecified', 'low', 'moderate', 'high')");
            tb.HasCheckConstraint(
                "ck_species_intra_aggression",
                "intra_aggression IN ('unspecified', 'low', 'moderate', 'high')");
            tb.HasCheckConstraint(
                "ck_species_diet",
                "diet IN ('unspecified', 'omnivore', 'carnivore', 'herbivore', 'limnivore')");
        });
    }
}
