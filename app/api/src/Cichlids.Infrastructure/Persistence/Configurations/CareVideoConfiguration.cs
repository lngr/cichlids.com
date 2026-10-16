using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CareVideoConfiguration : IEntityTypeConfiguration<CareVideo>
{
    public void Configure(EntityTypeBuilder<CareVideo> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.MediaItemId).IsUnique();
        builder.HasIndex(x => x.CareEntryId);
        builder.ToTable("care_video");
    }
}
