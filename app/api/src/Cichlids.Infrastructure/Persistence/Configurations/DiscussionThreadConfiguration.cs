using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class DiscussionThreadConfiguration : IEntityTypeConfiguration<DiscussionThread>
{
    public void Configure(EntityTypeBuilder<DiscussionThread> builder)
    {
        builder.ToTable("discussion_thread");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Category).HasConversion<SnakeCaseEnumConverter<DiscussionCategory>>().IsRequired();
        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<DiscussionThreadState>>().IsRequired();
        builder.Property(x => x.PostCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint(
                "ck_discussion_thread_category",
                "category IN ('cichlids', 'african', 'market_place')");
            tb.HasCheckConstraint(
                "ck_discussion_thread_state",
                "state IN ('archived', 'open')");
        });
    }
}
