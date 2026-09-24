using System.Globalization;
using System.Text.RegularExpressions;

namespace Banking.Domain.Common;

/// <summary>
/// Valor em menor unidade (centavos) com moeda explícita (ADR-002). Pode ser negativo: contas de sistema ficam negativas.
/// </summary>
public sealed partial record Money : IComparable<Money>
{
    private const int MaxIntegerDigits = 15;

    private Money(long minorUnits, Currency currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    public long MinorUnits { get; }

    public Currency Currency { get; }

    public bool IsPositive => MinorUnits > 0;

    public bool IsNegative => MinorUnits < 0;

    public static Money FromMinor(long minorUnits, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(minorUnits, currency);
    }

    public static Money Zero(Currency currency) => FromMinor(0, currency);

    /// <summary>
    /// Aceita só o formato canônico positivo ("100", "100.5", "100.50"). Escala acima do expoente é rejeitada, nunca arredondada.
    /// </summary>
    public static bool TryParse(string? text, Currency currency, out Money money)
    {
        ArgumentNullException.ThrowIfNull(currency);
        money = Zero(currency);

        if (text is null)
        {
            return false;
        }

        var match = AmountPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var integerPart = match.Groups["int"].Value;
        var fractionPart = match.Groups["frac"].Value;
        if (integerPart.Length > MaxIntegerDigits || fractionPart.Length > currency.MinorUnitExponent)
        {
            return false;
        }

        var fractionDigits = fractionPart.PadRight(currency.MinorUnitExponent, '0');
        var minor = long.Parse(integerPart, CultureInfo.InvariantCulture) * Pow10(currency.MinorUnitExponent)
            + (fractionDigits.Length == 0 ? 0 : long.Parse(fractionDigits, CultureInfo.InvariantCulture));

        money = new Money(minor, currency);
        return true;
    }

    public static Money Parse(string? text, Currency currency) =>
        TryParse(text, currency, out var money)
            ? money
            : throw new DomainException("invalid_amount", $"Valor inválido para {currency.Code}: '{text}'.");

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(MinorUnits + other.MinorUnits), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(MinorUnits - other.MinorUnits), Currency);
    }

    public Money Negate() => new(checked(-MinorUnits), Currency);

    public int CompareTo(Money? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Formato da API: sempre com as casas do expoente ("100.00", "-5.10").</summary>
    public string ToDecimalString()
    {
        var factor = Pow10(Currency.MinorUnitExponent);
        var absolute = Math.Abs((decimal)MinorUnits);
        var integer = decimal.Truncate(absolute / factor);
        var fraction = absolute - (integer * factor);
        var sign = MinorUnits < 0 ? "-" : string.Empty;

        return Currency.MinorUnitExponent == 0
            ? $"{sign}{integer.ToString(CultureInfo.InvariantCulture)}"
            : $"{sign}{integer.ToString(CultureInfo.InvariantCulture)}.{fraction.ToString(CultureInfo.InvariantCulture).PadLeft(Currency.MinorUnitExponent, '0')}";
    }

    public override string ToString() => $"{Currency.Code} {ToDecimalString()}";

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new DomainException(
                "currency_mismatch",
                $"Operação entre moedas diferentes: {Currency.Code} e {other.Currency.Code}.");
        }
    }

    private static long Pow10(int exponent)
    {
        long result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<int>0|[1-9][0-9]*)(\.(?<frac>[0-9]+))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();
}
