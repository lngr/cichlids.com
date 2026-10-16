using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class SlugAliasConfiguration : IEntityTypeConfiguration<SlugAlias>
{
    public void Configure(EntityTypeBuilder<SlugAlias> builder)
    {
        builder.ToTable("slug_alias");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.IsCanonical).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.Value).IsUnique();

        // One canonical alias per story at most; every other alias for the story stays resolvable
        // without being unique on its own.
        builder.HasIndex(x => x.StoryId)
            .IsUnique()
            .HasFilter("is_canonical = true");

        builder.HasOne<Story>()
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
