using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class UsedChecksumConfiguration : IEntityTypeConfiguration<UsedChecksum>
{
    public void Configure(EntityTypeBuilder<UsedChecksum> builder)
    {
        builder.HasKey(x => new { x.ProfileId, x.ChecksumSha256, x.Kind });
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<UsedChecksumKind>>().IsRequired();
        builder.Property(x => x.FirstUsedAt).IsRequired().HasDefaultValueSql("now()");
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("used_checksum", table =>
            table.HasCheckConstraint("ck_used_checksum_kind", "kind IN ('water_change_evidence', 'proof')"));
    }
}
