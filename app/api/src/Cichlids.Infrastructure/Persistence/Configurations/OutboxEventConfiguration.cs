using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_event");

        builder.HasKey(x => x.Id);

        // The writer generates the id (a version 7 UUID) before insert, so the database must
        // not generate its own default for it.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.EventType).IsRequired();
        builder.Property(x => x.AggregateType).IsRequired();
        builder.Property(x => x.AggregateId).IsRequired();
        builder.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
    }
}
