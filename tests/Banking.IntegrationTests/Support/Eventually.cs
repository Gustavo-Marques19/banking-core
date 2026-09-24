using Xunit;

namespace Banking.IntegrationTests.Support;

/// <summary>Espera uma condição assíncrona (worker, broker) ficar verdadeira, com prazo.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<Task<bool>> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Condição não atingida no prazo: {description}");
    }
}
