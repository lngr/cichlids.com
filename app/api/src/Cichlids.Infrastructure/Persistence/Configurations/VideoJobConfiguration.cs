using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class VideoJobConfiguration : IEntityTypeConfiguration<VideoJob>
{
    public void Configure(EntityTypeBuilder<VideoJob> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<SnakeCaseEnumConverter<VideoJobStatus>>().IsRequired();
        builder.HasOne<MediaItem>().WithOne(m => m.VideoJob).HasForeignKey<VideoJob>(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.MediaItemId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.RunAfter });
        builder.ToTable("video_job", table =>
        {
            table.HasCheckConstraint("ck_video_job_status", "status IN ('queued', 'running', 'done', 'failed')");
            table.HasCheckConstraint("ck_video_job_attempts", "attempts >= 0");
        });
    }
}
