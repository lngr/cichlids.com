using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<SnakeCaseEnumConverter<FollowStatus>>().HasDefaultValue(FollowStatus.Approved)
            .ValueGeneratedNever().IsRequired();

        builder.HasIndex(x => new { x.FollowerProfileId, x.TargetProfileId }).IsUnique();
        builder.HasIndex(x => new { x.FollowerProfileId, x.TargetTankId }).IsUnique();
        builder.HasIndex(x => new { x.TargetTankId, x.Status });

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.FollowerProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.TargetProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tank>()
            .WithMany()
            .HasForeignKey(x => x.TargetTankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("follow", tb =>
        {
            tb.HasCheckConstraint("ck_follow_exactly_one_target", "(target_profile_id IS NULL) <> (target_tank_id IS NULL)");
            tb.HasCheckConstraint("ck_follow_status", "status IN ('pending', 'approved')");
            tb.HasCheckConstraint("ck_follow_pending_tank", "status = 'approved' OR target_tank_id IS NOT NULL");
        });
    }
}
