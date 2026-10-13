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

        // The default only covers raw inserts; the writer sets the next attempt to the time the
        // event occurred.
        builder.Property(x => x.NextAttemptAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.AcknowledgedSubscriptionIds).IsRequired().HasDefaultValueSql("'{}'");

        // The dispatcher looks up due events only among open ones, which stay few while the
        // table of dispatched events grows.
        builder.HasIndex(x => x.NextAttemptAt)
            .HasDatabaseName("ix_outbox_event_due")
            .HasFilter("dispatched_at IS NULL AND given_up_at IS NULL");
    }
}
