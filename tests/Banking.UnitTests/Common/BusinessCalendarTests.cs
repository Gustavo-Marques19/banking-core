using Banking.Domain.Common;
using Xunit;

namespace Banking.UnitTests.Common;

public sealed class BusinessCalendarTests
{
    [Fact]
    public void Data_contabil_segue_o_horario_de_brasilia()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2025, 12, 31), BusinessCalendar.DateOf(instant));
    }
}
