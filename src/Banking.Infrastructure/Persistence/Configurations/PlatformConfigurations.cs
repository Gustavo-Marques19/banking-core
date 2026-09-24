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
