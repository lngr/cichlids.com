using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class InhabitantConfiguration : IEntityTypeConfiguration<Inhabitant>
{
    public void Configure(EntityTypeBuilder<Inhabitant> builder)
    {
        builder.ToTable("inhabitant");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasOne<Tank>()
            .WithMany(x => x.Inhabitants)
            .HasForeignKey(x => x.TankId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Species>()
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
