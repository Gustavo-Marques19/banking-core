using Banking.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", DatabaseSchemas.Accounts);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.Subject).IsUnique();
        builder.HasIndex(c => c.DocumentBlindIndex).IsUnique();

        builder.Property(c => c.Subject).HasMaxLength(100);
        builder.Property(c => c.Name).HasMaxLength(120);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public const string NumberSequence = "account_number_seq";

    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", DatabaseSchemas.Accounts);
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.Number).IsUnique();
        builder.HasIndex(a => a.LedgerAccountId).IsUnique();
        builder.HasIndex(a => a.CustomerId);

        builder.Property(a => a.Branch).HasMaxLength(4);
        builder.Property(a => a.Number).HasMaxLength(20);
        builder.Property(a => a.CurrencyCode).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(a => a.Currency);

        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
