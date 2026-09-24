namespace Banking.Demo;

// Classe com Main explícito: top-level statements gerariam um Program que conflita com o da API.
internal static class DemoProgram
{
    public static async Task<int> Main(string[] args)
    {
        var bench = args.Contains("--bench");
        var report = new Report();

        Console.WriteLine(bench ? "Banking Core: benchmark de transferências internas" : "Banking Core: demo dos cenários da spec");
        Console.WriteLine("Subindo Postgres e RabbitMQ (Testcontainers) e a API em processo...");

        await using var environment = await DemoEnvironment.StartAsync();
        var bank = new DemoBank(environment);

        if (bench)
        {
            await Benchmarks.RunAsync(bank, report);
        }
        else
        {
            await Scenarios.RunAllAsync(bank, report);
        }

        report.Summary();
        return report.ExitCode;
    }
}
