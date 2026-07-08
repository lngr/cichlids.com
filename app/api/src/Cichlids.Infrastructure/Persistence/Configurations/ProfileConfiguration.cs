using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username).IsRequired();
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<ProfileKind>>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();
        builder.HasIndex(x => x.Username).IsUnique();

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.ProfileImageMediaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.AvatarMediaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("profile", tb => tb.HasCheckConstraint(
            "ck_profile_kind",
            "kind IN ('member', 'archived', 'system')"));
    }
}
