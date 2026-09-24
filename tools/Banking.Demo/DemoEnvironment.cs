using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Banking.Infrastructure.Persistence;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Banking.Demo;

/// <summary>
/// Postgres e RabbitMQ reais (Testcontainers), com o mesmo script de roles do devcontainer, e a API hospedada no processo.
/// Tokens são assinados por uma chave gerada aqui: a demo mostra consistência e resiliência, não o login no Keycloak.
/// </summary>
internal sealed class DemoEnvironment : IAsyncDisposable
{
    public const string Issuer = "https://demo.banking.local/realms/banking";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "demo" };

    private readonly string _appPassword;

    private DemoEnvironment(PostgreSqlContainer postgres, RabbitMqContainer rabbit, string appPassword)
    {
        Postgres = postgres;
        Rabbit = rabbit;
        _appPassword = appPassword;
    }

    public PostgreSqlContainer Postgres { get; }

    public RabbitMqContainer Rabbit { get; }

    public DemoApi Api { get; private set; } = null!;

    public string AppConnectionString => ConnectionString("banking_app", _appPassword);

    public string SuperuserConnectionString => new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = "banking" }.ConnectionString;

    public static async Task<DemoEnvironment> StartAsync()
    {
        var root = RepositoryRoot();
        var migratorPassword = Guid.NewGuid().ToString("N");
        var appPassword = Guid.NewGuid().ToString("N");

        var postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
            .WithCommand("-c", "max_connections=400")
            .WithEnvironment("BANKING_MIGRATOR_PASSWORD", migratorPassword)
            .WithEnvironment("BANKING_APP_PASSWORD", appPassword)
            .WithResourceMapping(
                new FileInfo(Path.Combine(root, "infra", "postgres", "init", "01-roles.sh")),
                "/docker-entrypoint-initdb.d/",
                fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute
                    | UnixFileModes.GroupRead | UnixFileModes.GroupExecute | UnixFileModes.OtherRead | UnixFileModes.OtherExecute)
            .Build();
        var rabbit = new RabbitMqBuilder("rabbitmq:4.3.6-management-alpine").WithUsername("banking").WithPassword("demo-only").Build();

        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());

        var environment = new DemoEnvironment(postgres, rabbit, appPassword);

        var options = new DbContextOptionsBuilder<BankingDbContext>();
        BankingDbContextOptions.Configure(options, environment.ConnectionString("banking_migrator", migratorPassword));
        await using (var db = new BankingDbContext(options.Options))
        {
            await db.Database.MigrateAsync();
        }

        // Projetos de teste recebem esse caminho por um atributo gerado; um console comum precisa informar.
        System.Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_BANKING_API", Path.Combine(root, "src", "Banking.Api"));
        environment.Api = new DemoApi(environment);
        environment.Api.StartServer();
        return environment;
    }

    public HttpClient ClientFor(string subject, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "banking-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

        var client = Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await Rabbit.DisposeAsync();
        await Postgres.DisposeAsync();
    }

    private string ConnectionString(string username, string password) =>
        new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = "banking",
            Username = username,
            Password = password,
            MaxPoolSize = 200,
        }.ConnectionString;

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Banking.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Rode a demo de dentro do repositório (Banking.slnx não encontrado).");
    }

    internal sealed class DemoApi(DemoEnvironment environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            var settings = new Dictionary<string, string>
            {
                ["ConnectionStrings:Banking"] = environment.AppConnectionString,
                ["Pii:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Pii:BlindIndexKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Messaging:Uri"] = environment.Rabbit.GetConnectionString(),
                ["Messaging:PublishTimeout"] = "00:00:02",
                ["Outbox:PollInterval"] = "00:00:00.200",
                ["Outbox:BaseRetryDelay"] = "00:00:00.500",
                ["Outbox:MaxRetryDelay"] = "00:00:02",
                ["Consumers:ReconnectDelay"] = "00:00:00.500",
                ["ExternalTransfers:PollInterval"] = "00:00:00.200",
                ["ExternalTransfers:CheckInterval"] = "00:00:00.300",
                ["ExternalTransfers:MaxSubmitAttempts"] = "3",
                ["Provider:Timeout"] = "00:00:00.500",
                ["Database:LockTimeout"] = "00:00:15",
                ["Limits:DepositDailyPerOperator"] = "100000000.00",
                ["RateLimiting:TokenLimit"] = "1000000",
                ["RateLimiting:TokensPerPeriod"] = "1000000",
                ["Serilog:MinimumLevel:Default"] = "Error",
            };
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                }));
        }
    }
}
