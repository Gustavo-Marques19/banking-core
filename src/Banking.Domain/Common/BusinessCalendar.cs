namespace Banking.Domain.Common;

/// <summary>Data contábil e janela de limites diários seguem o horário de Brasília, não UTC.</summary>
public static class BusinessCalendar
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SaoPaulo).DateTime);
}
