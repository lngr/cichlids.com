using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class SpeciesLinkConfiguration : IEntityTypeConfiguration<SpeciesLink>
{
    public void Configure(EntityTypeBuilder<SpeciesLink> builder)
    {
        builder.ToTable("species_link");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Url).IsRequired();
        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasOne<Species>()
            .WithMany(x => x.Links)
            .HasForeignKey(x => x.SpeciesId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
