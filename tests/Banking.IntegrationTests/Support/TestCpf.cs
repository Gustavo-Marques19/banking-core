namespace Banking.IntegrationTests.Support;

/// <summary>Gera CPFs válidos e aleatórios. Nenhum CPF real entra nos testes.</summary>
internal static class TestCpf
{
    public static string New()
    {
        var digits = new int[11];
        do
        {
            for (var i = 0; i < 9; i++)
            {
                digits[i] = Random.Shared.Next(10);
            }
        }
        while (digits.Take(9).Distinct().Count() == 1);

        digits[9] = CheckDigit(digits, 9);
        digits[10] = CheckDigit(digits, 10);
        return string.Concat(digits);
    }

    public static string Formatted(string digits) => $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}";

    private static int CheckDigit(int[] digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += digits[i] * (length + 1 - i);
        }

        var remainder = sum * 10 % 11;
        return remainder == 10 ? 0 : remainder;
    }
}
