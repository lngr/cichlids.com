using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CarePhotoConfiguration : IEntityTypeConfiguration<CarePhoto>
{
    public void Configure(EntityTypeBuilder<CarePhoto> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.MediaItemId).IsUnique();
        builder.HasIndex(x => new { x.CareEntryId, x.Position }).IsUnique();
        builder.ToTable("care_photo", table =>
            table.HasCheckConstraint("ck_care_photo_position", $"position BETWEEN 0 AND {CarePhoto.MaxPerEntry - 1}"));
    }
}
