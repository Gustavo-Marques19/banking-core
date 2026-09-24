using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyRecord
{
    public string ClientId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public byte[] RequestHash { get; set; } = [];

    public string? Result { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable("idempotency_keys", DatabaseSchemas.Platform);
        builder.HasKey(k => new { k.ClientId, k.Operation, k.Key });
        builder.HasIndex(k => k.ExpiresAt);

        builder.Property(k => k.ClientId).HasMaxLength(100);
        builder.Property(k => k.Operation).HasMaxLength(60);
        builder.Property(k => k.Key).HasMaxLength(64);
        builder.Property(k => k.Result).HasColumnType("jsonb");
    }
}

/// <summary>
/// Posição na cadeia, hash anterior e hash ficam fora do modelo: quem preenche é o trigger (ADR-008, M7).
/// </summary>
internal sealed class AuditLogRecord
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string? TraceId { get; set; }

    public string Details { get; set; } = "{}";
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogRecord>
{
    public void Configure(EntityTypeBuilder<AuditLogRecord> builder)
    {
        builder.ToTable("audit_log", DatabaseSchemas.Platform);
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.ResourceId);
        builder.Property(a => a.Actor).HasMaxLength(100);
        builder.Property(a => a.Operation).HasMaxLength(60);
        builder.Property(a => a.ResourceType).HasMaxLength(40);
        builder.Property(a => a.ResourceId).HasMaxLength(64);
        builder.Property(a => a.Outcome).HasMaxLength(80);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.TraceId).HasMaxLength(32);
        builder.Property(a => a.Details).HasColumnType("jsonb");
    }
}
