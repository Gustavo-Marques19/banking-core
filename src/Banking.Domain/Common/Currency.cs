namespace Banking.Domain.Common;

/// <summary>Moeda ISO 4217 com o expoente da menor unidade (ADR-002). Só existem as da lista fechada.</summary>
public sealed record Currency
{
    public static readonly Currency Brl = new("BRL", 2);

    private static readonly Dictionary<string, Currency> Supported = new(StringComparer.Ordinal)
    {
        [Brl.Code] = Brl,
    };

    private Currency(string code, int minorUnitExponent)
    {
        Code = code;
        MinorUnitExponent = minorUnitExponent;
    }

    public string Code { get; }

    public int MinorUnitExponent { get; }

    public static Currency FromCode(string? code) =>
        TryFromCode(code, out var currency)
            ? currency
            : throw new DomainException("unsupported_currency", $"Moeda não suportada: '{code}'.");

    public static bool TryFromCode(string? code, out Currency currency)
    {
        if (code is not null && Supported.TryGetValue(code, out var found))
        {
            currency = found;
            return true;
        }

        currency = Brl;
        return false;
    }

    public override string ToString() => Code;
}
