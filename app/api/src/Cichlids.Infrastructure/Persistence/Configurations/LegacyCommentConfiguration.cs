using Cichlids.Domain.Archive;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class LegacyCommentConfiguration : IEntityTypeConfiguration<LegacyComment>
{
    public void Configure(EntityTypeBuilder<LegacyComment> builder)
    {
        builder.ToTable("legacy_comment", "archive");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LegacyTargetType).HasConversion<SnakeCaseEnumConverter<LegacyTargetType>>().IsRequired();
        builder.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.VaultReason).IsRequired();
        builder.Property(x => x.ImportedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.ToTable(tb => tb.HasCheckConstraint(
            "ck_legacy_comment_target_type",
            "legacy_target_type IN ('picture', 'tank')"));
    }
}
