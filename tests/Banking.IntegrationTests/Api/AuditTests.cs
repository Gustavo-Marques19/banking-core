using System.Net;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>Trilha de auditoria com hash encadeado. Banco isolado: os testes adulteram a cadeia de propósito.</summary>
public sealed class AuditTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string IntegrityViolation = "23000";
    private const string InsufficientPrivilege = "42501";

    private IsolatedDatabase _database = null!;
    private ApiFactory _api = null!;
    private TestBank _bank = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateIsolatedDatabaseAsync();
        _api = new ApiFactory(_database.AppConnectionString);
        _bank = new TestBank(_api);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Transferencia_fica_auditada_com_ator_e_trace_da_requisicao()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (_, to) = await _bank.NewAccountAsync();

        var response = await Transfers.SendAsync(_bank, alice, from, to, "10.00");
        var transferId = (await TestBank.ReadAsync(response)).GetProperty("id").GetGuid();
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();

        var entries = await TestBank.ReadAsync(await _bank.Client(_bank.Operator).GetAsync($"/api/v1/admin/audit?resourceId={transferId}", Ct));
        var entry = Assert.Single(entries.EnumerateArray());
        Assert.Equal("transfer.create", entry.GetProperty("operation").GetString());
        Assert.Equal(alice.Subject, entry.GetProperty("actor").GetString());
        Assert.Equal("completed", entry.GetProperty("outcome").GetString());
        Assert.Equal(traceId, entry.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Alteracao_feita_por_superusuario_e_detectada()
    {
        await _bank.NewAccountAsync("100.00");
        await _bank.NewAccountAsync("50.00");
        var admin = TestUser.NewAdmin();

        var before = await VerifyAsync(admin);
        Assert.True(before.GetProperty("isIntact").GetBoolean());
        Assert.True(before.GetProperty("verifiedEntries").GetInt64() >= 4);

        await AsSuperuserWithoutTriggersAsync("UPDATE platform.audit_log SET actor = 'mallory' WHERE chain_position = 2");

        var after = await VerifyAsync(admin);
        Assert.False(after.GetProperty("isIntact").GetBoolean());
        Assert.Equal(2, after.GetProperty("brokenAtPosition").GetInt64());
    }

    [Fact]
    public async Task Registro_apagado_por_superusuario_e_detectado()
    {
        await _bank.NewAccountAsync("100.00");
        var admin = TestUser.NewAdmin();

        await AsSuperuserWithoutTriggersAsync("DELETE FROM platform.audit_log WHERE chain_position = 2");

        var result = await VerifyAsync(admin);
        Assert.False(result.GetProperty("isIntact").GetBoolean());
        Assert.Equal(2, result.GetProperty("brokenAtPosition").GetInt64());
    }

    [Fact]
    public async Task Aplicacao_e_dono_nao_alteram_a_trilha()
    {
        await _bank.NewAccountAsync();

        await using var app = await Db.OpenAsync(_database.AppConnectionString);
        await Db.AssertFailsAsync(() => Db.ExecuteAsync(app, "UPDATE platform.audit_log SET actor = 'x'"), InsufficientPrivilege);
        await Db.AssertFailsAsync(() => Db.ExecuteAsync(app, "DELETE FROM platform.audit_log"), InsufficientPrivilege);

        await using var owner = await Db.OpenAsync(_database.MigratorConnectionString);
        await Db.AssertFailsAsync(() => Db.ExecuteAsync(owner, "UPDATE platform.audit_log SET actor = 'x'"), IntegrityViolation);
        await Db.AssertFailsAsync(() => Db.ExecuteAsync(owner, "DELETE FROM platform.audit_log"), IntegrityViolation);
    }

    [Fact]
    public async Task So_admin_verifica_a_trilha()
    {
        var response = await _bank.Client(_bank.Operator).GetAsync("/api/v1/admin/audit/verify", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<System.Text.Json.JsonElement> VerifyAsync(TestUser admin)
    {
        var response = await _bank.Client(admin).GetAsync("/api/v1/admin/audit/verify", Ct);
        await TestBank.EnsureStatusAsync(response, HttpStatusCode.OK);
        return await TestBank.ReadAsync(response);
    }

    /// <summary>Superusuário desligando triggers: o ataque que a prevenção não segura e a detecção precisa pegar.</summary>
    private async Task AsSuperuserWithoutTriggersAsync(string sql)
    {
        await using var superuser = await Db.OpenAsync(_database.SuperuserConnectionString);
        await Db.ExecuteAsync(superuser, "SET session_replication_role = replica");
        Assert.Equal(1, await Db.ExecuteAsync(superuser, sql));
    }
}
