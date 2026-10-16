using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class VideoUploadConfiguration : IEntityTypeConfiguration<VideoUpload>
{
    public void Configure(EntityTypeBuilder<VideoUpload> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Target).HasConversion<SnakeCaseEnumConverter<VideoUploadTarget>>().IsRequired();
        builder.Property(x => x.StorageKey).IsRequired();
        builder.Property(x => x.MultipartUploadId).IsRequired();
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.OwnerProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.OwnerProfileId);
        builder.HasIndex(x => x.ExpiresAt);
        builder.ToTable("video_upload", table =>
        {
            table.HasCheckConstraint("ck_video_upload_target", "target IN ('tank', 'care_entry')");
            table.HasCheckConstraint("ck_video_upload_offset", "\"offset\" >= 0 AND \"offset\" <= byte_size AND byte_size > 0 AND chunk_size > 0");
        });
    }
}
