using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace Banking.Demo;

/// <summary>
/// Latência e vazão das transferências internas, medidas no cliente (API em processo, Postgres em container).
/// Os números entram na ADR-004. Servem para comparar desenhos, não como capacidade de produção.
/// </summary>
internal static class Benchmarks
{
    private const int Concurrency = 50;

    public static async Task RunAsync(DemoBank bank, Report report)
    {
        Report.Section($"Ambiente: {Environment.ProcessorCount} CPUs, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

        var (warmClient, warmFrom) = await bank.NewAccountAsync("1000.00");
        var (_, warmTo) = await bank.NewAccountAsync();
        await RunAsync(50, _ => bank.TransferAsync(warmClient, warmFrom, warmTo, "1.00"));

        var (hotClient, hotFrom) = await bank.NewAccountAsync("10000.00");
        var (_, hotTo) = await bank.NewAccountAsync();
        var contention = await RunAsync(300, _ => bank.TransferAsync(hotClient, hotFrom, hotTo, "1.00"));
        Print("Uma conta de origem disputada (lock serializa)", contention, report);

        var pairs = new List<(HttpClient Client, Guid From, Guid To)>();
        for (var i = 0; i < 50; i++)
        {
            var (client, from) = await bank.NewAccountAsync("10000.00");
            var (_, to) = await bank.NewAccountAsync();
            pairs.Add((client, from, to));
        }

        var spread = await RunAsync(1000, i => bank.TransferAsync(pairs[i % pairs.Count].Client, pairs[i % pairs.Count].From, pairs[i % pairs.Count].To, "1.00"));
        Print("50 pares de contas independentes", spread, report);
    }

    private static async Task<Result> RunAsync(int total, Func<int, Task<HttpResponseMessage>> operation)
    {
        var latencies = new double[total];
        var failures = 0;
        using var gate = new SemaphoreSlim(Concurrency);
        var clock = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, total).Select(async i =>
        {
            await gate.WaitAsync();
            try
            {
                var started = Stopwatch.GetTimestamp();
                var response = await operation(i);
                latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (response.StatusCode != HttpStatusCode.Created)
                {
                    Interlocked.Increment(ref failures);
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        clock.Stop();
        Array.Sort(latencies);
        return new Result(total, failures, clock.Elapsed, latencies);
    }

    private static void Print(string title, Result result, Report report)
    {
        Report.Section($"{title}: {result.Total} transferências, {Concurrency} simultâneas");
        Report.Line(string.Create(CultureInfo.InvariantCulture,
            $"p50 {result.Percentile(50):F1} ms | p95 {result.Percentile(95):F1} ms | p99 {result.Percentile(99):F1} ms | máx {result.Latencies[^1]:F1} ms"));
        Report.Line(string.Create(CultureInfo.InvariantCulture, $"vazão {result.Total / result.Elapsed.TotalSeconds:F0} transferências/s"));
        report.Check(result.Failures == 0, "todas concluídas");
    }

    private sealed record Result(int Total, int Failures, TimeSpan Elapsed, double[] Latencies)
    {
        public double Percentile(int p) => Latencies[Math.Min(Latencies.Length - 1, (int)Math.Ceiling(p / 100.0 * Latencies.Length) - 1)];
    }
}
