using Banking.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Banking.Infrastructure.Persistence.Configurations;

internal static class LedgerConverters
{
    public static readonly ValueConverter<EntryDirection, string> Direction = new(
        v => v == EntryDirection.Debit ? "D" : "C",
        v => v == "D" ? EntryDirection.Debit : EntryDirection.Credit);

    public static readonly ValueConverter<LedgerAccountNature, string> Nature = new(
        v => v == LedgerAccountNature.Asset ? "ASSET" : "LIABILITY",
        v => v == "ASSET" ? LedgerAccountNature.Asset : LedgerAccountNature.Liability);
}

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts", DatabaseSchemas.Ledger, t =>
        {
            t.HasCheckConstraint("ck_ledger_accounts_normal_balance", "normal_balance IN ('D', 'C')");
            t.HasCheckConstraint("ck_ledger_accounts_nature", "nature IN ('ASSET', 'LIABILITY')");
            t.HasCheckConstraint("ck_ledger_accounts_materialized_only_for_customers", "has_materialized_balance = (account_id IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_ledger_accounts_customer_rules",
                "account_id IS NULL OR (nature = 'LIABILITY' AND normal_balance = 'C' AND NOT allow_negative)");
        });

        builder.HasKey(a => a.Id);
        builder.HasAlternateKey(a => new { a.Id, a.CurrencyCode });
        builder.HasIndex(a => a.Code).IsUnique();
        builder.HasIndex(a => a.AccountId).IsUnique();

        builder.Property(a => a.Code).HasMaxLength(40);
        builder.Property(a => a.Name).HasMaxLength(120);
        builder.Property(a => a.Nature).HasConversion(LedgerConverters.Nature).HasMaxLength(10);
        builder.Property(a => a.NormalBalance).HasConversion(LedgerConverters.Direction).HasColumnType("char(1)");
        builder.Property(a => a.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Ignore(a => a.Currency);
    }
}

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions", DatabaseSchemas.Ledger);

        builder.HasKey(t => t.Id);
        builder.HasIndex(t => t.ExternalId).IsUnique();
        builder.HasIndex(t => t.ReversesTransactionId).IsUnique();

        builder.Property(t => t.ExternalId).HasMaxLength(120);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(t => t.Description).HasMaxLength(200);
        builder.Property(t => t.CorrelationId).HasMaxLength(64);

        builder.HasOne<LedgerTransaction>()
            .WithOne()
            .HasForeignKey<LedgerTransaction>(t => t.ReversesTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(t => t.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries", DatabaseSchemas.Ledger, t =>
        {
            t.HasCheckConstraint("ck_ledger_entries_amount_positive", "amount_minor > 0");
            t.HasCheckConstraint("ck_ledger_entries_direction", "direction IN ('D', 'C')");
            t.HasCheckConstraint(
                "ck_ledger_entries_sequence_and_balance_together",
                "(account_sequence IS NULL) = (balance_after_minor IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.LedgerAccountId, e.AccountSequence }).IsUnique();

        builder.Property(e => e.Direction).HasConversion(LedgerConverters.Direction).HasColumnType("char(1)");
        builder.Property(e => e.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Ignore(e => e.Amount);

        // AccountSequence e BalanceAfterMinor são preenchidos por trigger; o valor enviado pela aplicação é descartado.

        // Chave composta garante que o lançamento está na moeda da conta.
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(e => new { e.LedgerAccountId, e.CurrencyCode })
            .HasPrincipalKey(a => new { a.Id, a.CurrencyCode })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountBalanceRecord
{
    public Guid LedgerAccountId { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string NormalBalance { get; set; } = string.Empty;

    public long BalanceMinor { get; set; }

    public long LastSequence { get; set; }

    public bool AllowNegative { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class AccountBalanceConfiguration : IEntityTypeConfiguration<AccountBalanceRecord>
{
    public void Configure(EntityTypeBuilder<AccountBalanceRecord> builder)
    {
        builder.ToTable("account_balances", DatabaseSchemas.Ledger, t =>
            t.HasCheckConstraint("ck_account_balances_non_negative", "allow_negative OR balance_minor >= 0"));

        builder.HasKey(b => b.LedgerAccountId);
        builder.Property(b => b.Currency).HasColumnType("char(3)");
        builder.Property(b => b.NormalBalance).HasColumnType("char(1)");

        builder.HasOne<LedgerAccount>()
            .WithOne()
            .HasForeignKey<AccountBalanceRecord>(b => new { b.LedgerAccountId, b.Currency })
            .HasPrincipalKey<LedgerAccount>(a => new { a.Id, a.CurrencyCode })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
