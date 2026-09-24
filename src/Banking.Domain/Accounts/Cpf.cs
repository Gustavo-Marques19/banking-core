using Banking.Domain.Common;

namespace Banking.Domain.Accounts;

/// <summary>
/// CPF validado pelos dígitos verificadores. <see cref="ToString"/> devolve a versão mascarada,
/// então interpolar um <see cref="Cpf"/> em log não vaza o número.
/// </summary>
public sealed record Cpf
{
    private Cpf(string digits)
    {
        Digits = digits;
    }

    /// <summary>Os 11 dígitos, sem máscara. Use só para criptografar ou calcular o blind index.</summary>
    public string Digits { get; }

    public string Masked => $"***.{Digits[3..6]}.{Digits[6..9]}-**";

    public static Cpf Parse(string? text) =>
        TryParse(text, out var cpf) ? cpf : throw new DomainException("invalid_document", "CPF inválido.");

    public static bool TryParse(string? text, out Cpf cpf)
    {
        cpf = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 14)
        {
            return false;
        }

        var digits = new string([.. text.Where(char.IsAsciiDigit)]);
        var onlyAllowedSeparators = text.All(c => char.IsAsciiDigit(c) || c is '.' or '-');
        if (!onlyAllowedSeparators || digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        if (CheckDigit(digits, 9) != digits[9] - '0' || CheckDigit(digits, 10) != digits[10] - '0')
        {
            return false;
        }

        cpf = new Cpf(digits);
        return true;
    }

    public override string ToString() => Masked;

    private static int CheckDigit(string digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += (digits[i] - '0') * (length + 1 - i);
        }

        var remainder = sum * 10 % 11;
        return remainder == 10 ? 0 : remainder;
    }
}
