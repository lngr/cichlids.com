using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class PostSpeciesConfiguration : IEntityTypeConfiguration<PostSpecies>
{
    public void Configure(EntityTypeBuilder<PostSpecies> builder)
    {
        builder.ToTable("post_species");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasIndex(x => new { x.PostId, x.SpeciesId }).IsUnique();

        builder.HasOne<Post>()
            .WithMany(x => x.Species)
            .HasForeignKey(x => x.PostId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Species>()
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
