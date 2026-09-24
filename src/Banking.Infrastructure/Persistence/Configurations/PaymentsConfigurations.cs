using Banking.Domain.Accounts;
using Banking.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class DepositConfiguration : IEntityTypeConfiguration<Deposit>
{
    public void Configure(EntityTypeBuilder<Deposit> builder)
    {
        builder.ToTable("deposits", DatabaseSchemas.Payments, t =>
            t.HasCheckConstraint("ck_deposits_amount_positive", "amount_minor > 0"));
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.RequestedBy, d.IdempotencyKey }).IsUnique();
        builder.HasIndex(d => d.AccountId);

        builder.Property(d => d.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(d => d.Reason).HasMaxLength(200);
        builder.Property(d => d.RequestedBy).HasMaxLength(100);
        builder.Property(d => d.IdempotencyKey).HasMaxLength(64);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.RejectionReason).HasConversion<string>().HasMaxLength(40);
        builder.Property(d => d.DecidedBy).HasMaxLength(100);
        builder.Ignore(d => d.Amount);

        builder.HasOne<Account>().WithMany().HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InternalTransferConfiguration : IEntityTypeConfiguration<InternalTransfer>
{
    public void Configure(EntityTypeBuilder<InternalTransfer> builder)
    {
        builder.ToTable("internal_transfers", DatabaseSchemas.Payments, t =>
        {
            t.HasCheckConstraint("ck_internal_transfers_amount_positive", "amount_minor > 0");
            t.HasCheckConstraint("ck_internal_transfers_distinct_accounts", "source_account_id <> destination_account_id");
        });
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.RequestedBy, t.IdempotencyKey }).IsUnique();
        builder.HasIndex(t => t.SourceAccountId);
        builder.HasIndex(t => t.DestinationAccountId);

        builder.Property(t => t.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(t => t.Description).HasMaxLength(140);
        builder.Property(t => t.RequestedBy).HasMaxLength(100);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(64);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.RejectionReason).HasConversion<string>().HasMaxLength(40);
        builder.Ignore(t => t.Amount);

        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExternalTransferConfiguration : IEntityTypeConfiguration<ExternalTransfer>
{
    public void Configure(EntityTypeBuilder<ExternalTransfer> builder)
    {
        builder.ToTable("external_transfers", DatabaseSchemas.Payments, t =>
            t.HasCheckConstraint("ck_external_transfers_amount_positive", "amount_minor > 0"));
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.RequestedBy, t.IdempotencyKey }).IsUnique();
        builder.HasIndex(t => t.SourceAccountId);
        builder.HasIndex(t => new { t.Status, t.NextCheckAt });

        builder.Property(t => t.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(t => t.DestinationBank).HasMaxLength(8);
        builder.Property(t => t.DestinationBranch).HasMaxLength(4);
        builder.Property(t => t.DestinationAccount).HasMaxLength(30);
        builder.Property(t => t.RequestedBy).HasMaxLength(100);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(64);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.RejectionReason).HasConversion<string>().HasMaxLength(40);
        builder.Property(t => t.FailureReason).HasMaxLength(80);
        builder.Ignore(t => t.Amount);
        builder.Ignore(t => t.ClientReference);
        builder.Ignore(t => t.ReservationExternalId);
        builder.Ignore(t => t.ResolutionExternalId);
        builder.Ignore(t => t.IsTerminal);

        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransferReversalConfiguration : IEntityTypeConfiguration<TransferReversal>
{
    public void Configure(EntityTypeBuilder<TransferReversal> builder)
    {
        builder.ToTable("transfer_reversals", DatabaseSchemas.Payments);
        builder.HasKey(r => r.Id);

        // No máximo um estorno pendente ou concluído por transferência; recusados podem se repetir.
        builder.HasIndex(r => r.TransferId).IsUnique().HasFilter("status IN ('PendingApproval', 'Completed')");

        builder.Property(r => r.RequestedBy).HasMaxLength(100);
        builder.Property(r => r.Reason).HasMaxLength(200);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RejectionReason).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.DecidedBy).HasMaxLength(100);

        builder.HasOne<InternalTransfer>().WithMany().HasForeignKey(r => r.TransferId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ManualResolutionConfiguration : IEntityTypeConfiguration<ManualResolution>
{
    public void Configure(EntityTypeBuilder<ManualResolution> builder)
    {
        builder.ToTable("manual_resolutions", DatabaseSchemas.Payments);
        builder.HasKey(r => r.Id);

        // Uma resolução pendente por vez para a mesma transferência.
        builder.HasIndex(r => r.ExternalTransferId).IsUnique().HasFilter("status = 'PendingApproval'");

        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Evidence).HasMaxLength(500);
        builder.Property(r => r.RequestedBy).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DecidedBy).HasMaxLength(100);

        builder.HasOne<ExternalTransfer>().WithMany().HasForeignKey(r => r.ExternalTransferId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LimitUsageRecord
{
    public string LimitKind { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public DateOnly UsageDate { get; set; }

    public long UsedMinor { get; set; }
}

internal sealed class LimitUsageConfiguration : IEntityTypeConfiguration<LimitUsageRecord>
{
    public void Configure(EntityTypeBuilder<LimitUsageRecord> builder)
    {
        builder.ToTable("limit_usage", DatabaseSchemas.Payments, t =>
            t.HasCheckConstraint("ck_limit_usage_non_negative", "used_minor >= 0"));
        builder.HasKey(u => new { u.LimitKind, u.Subject, u.UsageDate });
        builder.Property(u => u.LimitKind).HasMaxLength(40);
        builder.Property(u => u.Subject).HasMaxLength(100);
    }
}
