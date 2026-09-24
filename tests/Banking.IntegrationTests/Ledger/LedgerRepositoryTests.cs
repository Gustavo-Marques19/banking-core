using Banking.Application.Abstractions;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Banking.IntegrationTests.Ledger;

public sealed class LedgerRepositoryTests(PostgresFixture postgres) : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(postgres.AppConnectionString)
        .BuildServiceProvider();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deposito_pelo_repositorio_trava_saldo_e_lanca()
    {
        var customer = await OpenCustomerAccountAsync();

        await using (var scope = _services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);

            var balances = await ledger.LockBalancesAsync([customer, SystemLedgerAccounts.Funding], Ct);
            Assert.Equal([customer], balances.Keys);
            Assert.Equal(0, balances[customer].Balance.MinorUnits);

            var amount = Money.FromMinor(25_000, Currency.Brl);
            ledger.Add(LedgerTransaction.Create(
                $"deposit-{customer:N}",
                LedgerTransactionType.Deposit,
                "depósito de teste",
                Now,
                [PostingLine.Debit(SystemLedgerAccounts.Funding, amount), PostingLine.Credit(customer, amount)]));
            await unitOfWork.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
            var stored = await ledger.FindTransactionByExternalIdAsync($"deposit-{customer:N}", Ct);

            Assert.NotNull(stored);
            var customerEntry = Assert.Single(stored.Entries, e => e.LedgerAccountId == customer);
            Assert.Equal(1, customerEntry.AccountSequence);
            Assert.Equal(25_000, customerEntry.BalanceAfterMinor);
            var fundingEntry = Assert.Single(stored.Entries, e => e.LedgerAccountId == SystemLedgerAccounts.Funding);
            Assert.Null(fundingEntry.AccountSequence);
        }
    }

    [Fact]
    public async Task Lock_de_saldo_fora_de_transacao_e_erro_de_programacao()
    {
        await using var scope = _services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ledger.LockBalancesAsync([SystemLedgerAccounts.Funding], Ct));
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private async Task<Guid> OpenCustomerAccountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();

        var account = LedgerAccount.OpenForCustomerAccount(
            Guid.NewGuid(), Guid.NewGuid(), "T" + Guid.NewGuid().ToString("N")[..20], Currency.Brl, Now);
        ledger.Add(account);
        await unitOfWork.SaveChangesAsync(Ct);
        return account.Id;
    }
}
