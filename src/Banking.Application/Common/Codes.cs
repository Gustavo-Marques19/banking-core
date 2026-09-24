using System.Text;
using Banking.Domain.Common;

namespace Banking.Application.Common;

public static class Codes
{
    /// <summary>InsufficientFunds vira "insufficient_funds": o formato estável que vai para a API.</summary>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    public static string? Of(RejectionReason? reason) => reason is { } value ? Of(value) : null;
}
