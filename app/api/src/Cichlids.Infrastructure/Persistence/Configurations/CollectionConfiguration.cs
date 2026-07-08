using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("collection");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.IsPublic).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
