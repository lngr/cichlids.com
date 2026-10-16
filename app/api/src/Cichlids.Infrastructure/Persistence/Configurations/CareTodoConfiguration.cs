using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CareTodoConfiguration : IEntityTypeConfiguration<CareTodo>
{
    public void Configure(EntityTypeBuilder<CareTodo> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.CareEntryId);
        builder.HasIndex(x => new { x.ProfileId, x.CreatedAt, x.Id }).IsDescending(false, true, true)
            .HasDatabaseName("ix_care_todo_profile_open").HasFilter("done_at IS NULL");
        builder.HasIndex(x => new { x.ProfileId, x.DoneAt, x.Id }).IsDescending(false, true, true)
            .HasDatabaseName("ix_care_todo_profile_done").HasFilter("done_at IS NOT NULL");
        builder.ToTable("care_todo", table =>
            table.HasCheckConstraint("ck_care_todo_text", $"char_length(text) BETWEEN 1 AND {CareTodo.MaxTextLength}"));
    }
}
