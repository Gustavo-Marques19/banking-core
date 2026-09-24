using System.Net;
using Npgsql;

namespace Banking.Demo;

internal static class Scenarios
{
    public static async Task RunAllAsync(DemoBank bank, Report report)
    {
        await MainScenarioAsync(bank, report);
        await IdempotencyAsync(bank, report);
        await DoubleSpendingAsync(bank, report);
        await BrokerOutageAsync(bank, report);
        await ProviderScenariosAsync(bank, report);
        await ReconciliationAsync(bank, report);
        await AuditTamperingAsync(bank, report);
    }

    private static async Task MainScenarioAsync(DemoBank bank, Report report)
    {
        Report.Section("1. Cenário principal (spec §27): saldo R$ 1.000, 100 transferências simultâneas de R$ 100");
        var (alice, from) = await bank.NewAccountAsync("1000.00");
        var (bruno, to) = await bank.NewAccountAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => bank.TransferAsync(alice, from, to, "100.00")));
        var completed = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var origin = await bank.BalanceAsync(alice, from);
        var destination = await bank.BalanceAsync(bruno, to);
        var trial = await bank.ScalarAsync(
            "SELECT coalesce(sum(CASE WHEN direction = 'D' THEN amount_minor ELSE -amount_minor END), 0) FROM ledger.ledger_entries");

        Report.Line($"concluídas: {completed}, recusadas por saldo: {rejected}");
        Report.Line($"saldo final da origem: {origin}, do destino: {destination}");
        report.Check(completed == 10, "exatamente 10 concluídas");
        report.Check(rejected == 90, "90 recusadas com insufficient_funds");
        report.Check(origin == "0.00" && destination == "1000.00", "saldo final R$ 0 na origem, sem saldo negativo");
        report.Check(trial == 0, "balancete: débitos = créditos");
    }

    private static async Task IdempotencyAsync(DemoBank bank, Report report)
    {
        Report.Section("2. Mesma Idempotency-Key em 100 requisições simultâneas");
        var (alice, from) = await bank.NewAccountAsync("1000.00");
        var (_, to) = await bank.NewAccountAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => bank.TransferAsync(alice, from, to, "100.00", "chave-da-demo")));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        var operations = await bank.ScalarAsync("SELECT count(*) FROM payments.internal_transfers WHERE source_account_id = @id", ("id", from));

        Report.Line($"respostas 201: {responses.Count(r => r.StatusCode == HttpStatusCode.Created)}, repetidas (Idempotent-Replayed): {responses.Count(r => r.Headers.Contains("Idempotent-Replayed"))}");
        report.Check(operations == 1, "1 operação financeira");
        report.Check(bodies.Distinct().Count() == 1, "100 respostas idênticas");
        report.Check(await bank.BalanceAsync(alice, from) == "900.00", "debitado uma vez só");
    }

    private static async Task DoubleSpendingAsync(DemoBank bank, Report report)
    {
        Report.Section("3. Double spending (spec §13): saldo R$ 100, duas transferências de R$ 80");
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (_, to) = await bank.NewAccountAsync();

        var responses = await Task.WhenAll(bank.TransferAsync(alice, from, to, "80.00"), bank.TransferAsync(alice, from, to, "80.00"));

        report.Check(responses.Count(r => r.StatusCode == HttpStatusCode.Created) == 1, "uma concluída");
        report.Check(responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity) == 1, "outra recusada por saldo");
        report.Check(await bank.BalanceAsync(alice, from) == "20.00", "saldo R$ 20, nunca negativo");
    }

    private static async Task BrokerOutageAsync(DemoBank bank, Report report)
    {
        Report.Section("4. Broker fora do ar (spec §25.4)");
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (_, to) = await bank.NewAccountAsync();
        await DemoBank.WaitAsync(async () => await bank.ScalarAsync("SELECT count(*) FROM platform.outbox_messages WHERE status <> 'Published'") == 0, TimeSpan.FromSeconds(60));

        await bank.Environment.Rabbit.PauseAsync();
        Report.Line("RabbitMQ pausado");
        var transfer = await bank.TransferAsync(alice, from, to, "10.00");
        var transferId = (await DemoBank.ReadAsync(transfer)).GetProperty("id").GetGuid().ToString();
        report.Check(transfer.StatusCode == HttpStatusCode.Created, "transferência concluída mesmo sem broker");

        await DemoBank.WaitAsync(async () => await AttemptsAsync(bank, transferId) >= 1, TimeSpan.FromSeconds(60));
        Report.Line($"evento pendente no outbox, tentativas de publicação: {await AttemptsAsync(bank, transferId)}");

        await bank.Environment.Rabbit.UnpauseAsync();
        Report.Line("RabbitMQ de volta");
        await DemoBank.WaitAsync(
            async () => await bank.ScalarAsync(
                "SELECT count(*) FROM notifications.notifications WHERE account_id = @id AND kind = 'transfer_received'", ("id", to)) == 1,
            TimeSpan.FromSeconds(90));
        report.Check(true, "evento publicado e notificação entregue depois da volta");
    }

    private static Task<long> AttemptsAsync(DemoBank bank, string transferId) =>
        bank.ScalarAsync(
            "SELECT coalesce(max(attempts), 0) FROM platform.outbox_messages WHERE payload->'data'->>'transferId' = @id AND status = 'Pending'",
            ("id", transferId));

    private static async Task ProviderScenariosAsync(DemoBank bank, Report report)
    {
        Report.Section("5. Transferência externa e falhas do provider (spec §18 e §19)");
        var (alice, from) = await bank.NewAccountAsync("1000.00");

        var expectations = new (string Scenario, string Status, string Description)[]
        {
            ("SUCCESS-1", "completed", "sucesso: liquida de Clearing para Settlement"),
            ("FAIL-1", "failed", "recusa do provider: estorno ligado à reserva"),
            ("LATE-1", "completed", "timeout, mas o provider processou: conclui sem débito duplo"),
            ("TIMEOUT-1", "unknown", "timeout sem processamento: continua UNKNOWN e vai para revisão manual"),
        };

        var ids = new List<(Guid Id, string Status, string Description)>();
        foreach (var (scenario, status, description) in expectations)
        {
            var response = await bank.ExternalTransferAsync(alice, from, "100.00", scenario);
            ids.Add(((await DemoBank.ReadAsync(response)).GetProperty("id").GetGuid(), status, description));
        }

        foreach (var (id, expected, description) in ids)
        {
            await DemoBank.WaitAsync(async () =>
            {
                var view = await DemoBank.ReadAsync(await alice.GetAsync($"/api/v1/external-transfers/{id}"));
                var status = view.GetProperty("status").GetString();
                return expected == "unknown" ? view.GetProperty("requiresManualReview").GetBoolean() : status == expected;
            }, TimeSpan.FromSeconds(60));
            report.Check(true, description);
        }

        var balance = await bank.BalanceAsync(alice, from);
        Report.Line($"saldo depois: {balance} (SUCCESS e LATE saíram, FAIL voltou, TIMEOUT segue reservado em Clearing)");
        report.Check(balance == "700.00", "saldo R$ 700: nenhum débito duplo, nenhum estorno indevido");
    }

    private static async Task ReconciliationAsync(DemoBank bank, Report report)
    {
        Report.Section("6. Reconciliação: balancete, projeção de saldo, Clearing e extrato do provider");
        var result = await bank.ReconcileAsync();
        foreach (var finding in result.GetProperty("findings").EnumerateArray())
        {
            Report.Line($"divergência: {finding.GetProperty("check").GetString()}: {finding.GetProperty("detail").GetString()}");
        }

        report.Check(result.GetProperty("isConsistent").GetBoolean(), "nenhuma divergência");
    }

    private static async Task AuditTamperingAsync(DemoBank bank, Report report)
    {
        Report.Section("7. Trilha de auditoria adulterada por superusuário");
        var before = await DemoBank.ReadAsync(await bank.Admin.GetAsync("/api/v1/admin/audit/verify"));
        Report.Line($"registros verificados: {before.GetProperty("verifiedEntries").GetInt64()}");
        report.Check(before.GetProperty("isIntact").GetBoolean(), "cadeia íntegra antes da adulteração");

        await using (var superuser = new NpgsqlConnection(bank.Environment.SuperuserConnectionString))
        {
            await superuser.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SET session_replication_role = replica; UPDATE platform.audit_log SET actor = 'mallory' WHERE chain_position = 5", superuser);
            await command.ExecuteNonQueryAsync();
        }

        Report.Line("superusuário desligou os triggers e trocou o autor do registro 5");
        var after = await DemoBank.ReadAsync(await bank.Admin.GetAsync("/api/v1/admin/audit/verify"));
        Report.Line($"verificação: {after.GetProperty("reason").GetString()} (posição {after.GetProperty("brokenAtPosition").GetInt64()})");
        report.Check(!after.GetProperty("isIntact").GetBoolean() && after.GetProperty("brokenAtPosition").GetInt64() == 5, "adulteração detectada na posição 5");
    }
}
