using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class SpeciesCommonNameConfiguration : IEntityTypeConfiguration<SpeciesCommonName>
{
    public void Configure(EntityTypeBuilder<SpeciesCommonName> builder)
    {
        builder.ToTable("species_common_name");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired();

        builder.HasIndex(x => new { x.SpeciesId, x.Name }).IsUnique();

        builder.HasOne<Species>()
            .WithMany(x => x.CommonNames)
            .HasForeignKey(x => x.SpeciesId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
