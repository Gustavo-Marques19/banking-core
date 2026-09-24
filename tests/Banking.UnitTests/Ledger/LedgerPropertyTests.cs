using Banking.Domain.Common;
using Banking.Domain.Ledger;
using CsCheck;
using Xunit;

namespace Banking.UnitTests.Ledger;

/// <summary>
/// Gera sequências aleatórias de depósitos, transferências e estornos e confere as invariantes do ledger ao final de cada uma.
/// </summary>
public sealed class LedgerPropertyTests
{
    private const int CustomerAccounts = 4;

    private static readonly Gen<Operation> OperationGen = Gen.Select(
        Gen.Int[0, 2], Gen.Int[0, CustomerAccounts - 1], Gen.Int[0, CustomerAccounts - 1], Gen.Long[1, 50_000],
        (kind, from, to, amount) => new Operation(kind, from, to, amount));

    [Fact]
    public void Qualquer_sequencia_de_operacoes_mantem_as_invariantes()
    {
        OperationGen.Array[1, 80].Sample(operations =>
        {
            var ledger = new InMemoryLedger(CustomerAccounts);

            foreach (var operation in operations)
            {
                ledger.Apply(operation);
            }

            ledger.AssertInvariants();
        });
    }

    internal sealed record Operation(int Kind, int From, int To, long AmountMinor);

    /// <summary>Reproduz a semântica dos triggers do banco usando só os tipos do domínio.</summary>
    private sealed class InMemoryLedger
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        private readonly Guid[] _customers;
        private readonly Dictionary<Guid, LedgerBalance> _balances = [];
        private readonly Dictionary<Guid, int> _sequences = [];
        private readonly List<LedgerTransaction> _transactions = [];
        private readonly HashSet<Guid> _reversed = [];
        private int _operations;

        public InMemoryLedger(int customers)
        {
            _customers = [.. Enumerable.Range(0, customers).Select(_ => Guid.NewGuid())];
            foreach (var id in _customers)
            {
                _balances[id] = new LedgerBalance(id, Money.Zero(Currency.Brl), EntryDirection.Credit, AllowNegative: false);
                _sequences[id] = 0;
            }
        }

        public void Apply(Operation operation)
        {
            var amount = Money.FromMinor(operation.AmountMinor, Currency.Brl);
            var externalId = $"op-{++_operations}";

            switch (operation.Kind)
            {
                case 0:
                    Post(LedgerTransaction.Create(externalId, LedgerTransactionType.Deposit, "depósito", Now,
                    [
                        PostingLine.Debit(SystemLedgerAccounts.Funding, amount),
                        PostingLine.Credit(_customers[operation.To], amount),
                    ]));
                    break;

                case 1 when operation.From != operation.To:
                    var source = _customers[operation.From];
                    if (_balances[source].CanPost(EntryDirection.Debit, amount))
                    {
                        Post(LedgerTransaction.Create(externalId, LedgerTransactionType.InternalTransfer, "transferência", Now,
                        [
                            PostingLine.Debit(source, amount),
                            PostingLine.Credit(_customers[operation.To], amount),
                        ]));
                    }

                    break;

                case 2:
                    var candidate = _transactions.LastOrDefault(t =>
                        t.Type == LedgerTransactionType.InternalTransfer && !_reversed.Contains(t.Id));
                    if (candidate is not null && CanPostAll(candidate.Reverse(externalId, LedgerTransactionType.TransferReversal, "estorno", Now)))
                    {
                        Post(candidate.Reverse(externalId, LedgerTransactionType.TransferReversal, "estorno", Now));
                        _reversed.Add(candidate.Id);
                    }

                    break;
            }
        }

        public void AssertInvariants()
        {
            var entries = _transactions.SelectMany(t => t.Entries).ToList();

            Assert.Equal(
                entries.Where(e => e.Direction == EntryDirection.Debit).Sum(e => e.AmountMinor),
                entries.Where(e => e.Direction == EntryDirection.Credit).Sum(e => e.AmountMinor));

            foreach (var customer in _customers)
            {
                var own = entries.Where(e => e.LedgerAccountId == customer).ToList();
                var fromEntries = own.Sum(e => e.Direction == EntryDirection.Credit ? e.AmountMinor : -e.AmountMinor);

                Assert.Equal(fromEntries, _balances[customer].Balance.MinorUnits);
                Assert.False(_balances[customer].Balance.IsNegative);
                Assert.Equal(own.Count, _sequences[customer]);
            }

            Assert.Equal(_transactions.Count, _transactions.Select(t => t.ExternalId).Distinct().Count());
        }

        private bool CanPostAll(LedgerTransaction transaction) =>
            transaction.Entries
                .Where(e => _balances.ContainsKey(e.LedgerAccountId))
                .All(e => _balances[e.LedgerAccountId].CanPost(e.Direction, e.Amount));

        private void Post(LedgerTransaction transaction)
        {
            foreach (var entry in transaction.Entries.Where(e => _balances.ContainsKey(e.LedgerAccountId)))
            {
                var current = _balances[entry.LedgerAccountId];
                _balances[entry.LedgerAccountId] = current with { Balance = current.BalanceAfter(entry.Direction, entry.Amount) };
                _sequences[entry.LedgerAccountId]++;
            }

            _transactions.Add(transaction);
        }
    }
}
