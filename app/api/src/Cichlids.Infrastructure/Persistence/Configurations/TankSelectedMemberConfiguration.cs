using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class TankSelectedMemberConfiguration : IEntityTypeConfiguration<TankSelectedMember>
{
    public void Configure(EntityTypeBuilder<TankSelectedMember> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AddedAt).IsRequired();
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.MemberProfileId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TankId, x.MemberProfileId }).IsUnique();
        builder.HasIndex(x => x.MemberProfileId);
        builder.ToTable("tank_selected_member");
    }
}
