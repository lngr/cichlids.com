using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class InhabitantConfiguration : IEntityTypeConfiguration<Inhabitant>
{
    public const int NameMaxLength = 80;

    public void Configure(EntityTypeBuilder<Inhabitant> builder)
    {
        builder.ToTable("inhabitant", table =>
        {
            table.HasCheckConstraint("ck_inhabitant_name_count", "name IS NULL OR (count IS NOT NULL AND count = 1)");
            table.HasCheckConstraint("ck_inhabitant_name_not_blank", "name IS NULL OR length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_inhabitant_removal", "(removed_on IS NULL) = (removal_reason IS NULL)");
            table.HasCheckConstraint("ck_inhabitant_removal_reason", "removal_reason IS NULL OR removal_reason IN ('died', 'given_away', 'moved')");
            table.HasCheckConstraint("ck_inhabitant_dates", "removed_on IS NULL OR moved_in_on IS NULL OR removed_on >= moved_in_on");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.Name).HasMaxLength(NameMaxLength);
        builder.Property(x => x.RemovalReason).HasConversion<SnakeCaseEnumConverter<InhabitantRemovalReason>>();

        // Tanks are soft deleted, so no request removes a tank row. The restriction keeps a named fish
        // from vanishing with a hard delete; the cleanup of provisional accounts deletes the inhabitants first.
        builder.HasOne<Tank>()
            .WithMany(x => x.Inhabitants)
            .HasForeignKey(x => x.TankId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.OwnerProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Species>()
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TankId, x.RemovedOn });
        builder.HasIndex(x => x.OwnerProfileId);
    }
}
