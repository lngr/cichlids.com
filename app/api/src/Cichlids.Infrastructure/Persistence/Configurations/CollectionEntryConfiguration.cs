using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class CollectionEntryConfiguration : IEntityTypeConfiguration<CollectionEntry>
{
    public void Configure(EntityTypeBuilder<CollectionEntry> builder)
    {
        builder.ToTable("collection_entry");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasIndex(x => new { x.CollectionId, x.PostId }).IsUnique();

        builder.HasOne<Collection>()
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.CollectionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(x => x.PostId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
