using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Published = "Published";
    public const string DeadLettered = "DeadLettered";
}

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string Status { get; set; } = OutboxStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string? LastError { get; set; }

    public string? TraceParent { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        builder.ToTable("outbox_messages", DatabaseSchemas.Platform, t =>
            t.HasCheckConstraint("ck_outbox_messages_status", "status IN ('Pending', 'Published', 'DeadLettered')"));
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt }).HasFilter("status = 'Pending'");
        builder.Property(m => m.Type).HasMaxLength(80);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.Status).HasMaxLength(20);
        builder.Property(m => m.LastError).HasMaxLength(500);
        builder.Property(m => m.TraceParent).HasMaxLength(64);
    }
}

internal sealed class InboxMessageRecord
{
    public string Consumer { get; set; } = string.Empty;

    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<InboxMessageRecord> builder)
    {
        builder.ToTable("inbox_messages", DatabaseSchemas.Platform);
        builder.HasKey(m => new { m.Consumer, m.EventId });
        builder.Property(m => m.Consumer).HasMaxLength(60);
    }
}

internal sealed class NotificationRecord
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public Guid AccountId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? Amount { get; set; }

    public string? Currency { get; set; }

    public Guid ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("notifications", DatabaseSchemas.Notifications);
        builder.HasKey(n => n.Id);
        builder.HasIndex(n => new { n.EventId, n.AccountId, n.Kind }).IsUnique();
        builder.HasIndex(n => new { n.AccountId, n.CreatedAt });
        builder.Property(n => n.Kind).HasMaxLength(60);
        builder.Property(n => n.Amount).HasMaxLength(24);
        builder.Property(n => n.Currency).HasColumnType("char(3)");
    }
}
