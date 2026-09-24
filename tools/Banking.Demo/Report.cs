namespace Banking.Demo;

internal sealed class Report
{
    private int _failures;

    public int ExitCode => _failures == 0 ? 0 : 1;

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }

    public static void Line(string text) => Console.WriteLine($"   {text}");

    public void Check(bool condition, string description)
    {
        Console.WriteLine($"   {(condition ? "OK     " : "FALHOU ")} {description}");
        if (!condition)
        {
            _failures++;
        }
    }

    public void Summary()
    {
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "Todas as verificações passaram." : $"{_failures} verificação(ões) falharam.");
    }
}
