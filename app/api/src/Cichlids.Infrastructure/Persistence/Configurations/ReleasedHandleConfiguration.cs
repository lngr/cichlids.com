using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class ReleasedHandleConfiguration : IEntityTypeConfiguration<ReleasedHandle>
{
    public void Configure(EntityTypeBuilder<ReleasedHandle> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Handle).IsRequired();
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.Handle).IsUnique();
        builder.HasIndex(x => x.ProfileId);
        builder.ToTable("released_handle", table =>
        {
            table.HasCheckConstraint("ck_released_handle_lowercase", "handle = lower(handle)");
            table.HasCheckConstraint("ck_released_handle_period", "reserved_until > released_at");
        });
    }
}
