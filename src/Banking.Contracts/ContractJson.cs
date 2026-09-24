using System.Text.Json;
using System.Text.Json.Serialization;

namespace Banking.Contracts;

/// <summary>Regras de JSON da API: campo desconhecido, campo obrigatório ausente e null indevido viram 400.</summary>
public static class ContractJson
{
    public static JsonSerializerOptions Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        return options;
    }
}
