using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Banking.ArchitectureTests;

/// <summary>
/// Pega dependências que chegam por caminho transitivo, que o teste de .csproj não enxerga.
/// </summary>
public sealed class TypeDependencyTests
{
    public static TheoryData<string, string> ForbiddenDependencies => new()
    {
        { "Banking.Domain", "Banking.Application" },
        { "Banking.Domain", "Banking.Infrastructure" },
        { "Banking.Domain", "Banking.Api" },
        { "Banking.Domain", "Banking.Contracts" },
        { "Banking.Domain", "Microsoft.EntityFrameworkCore" },
        { "Banking.Domain", "Microsoft.AspNetCore" },
        { "Banking.Domain", "Npgsql" },
        { "Banking.Application", "Banking.Infrastructure" },
        { "Banking.Application", "Banking.Api" },
        { "Banking.Application", "Microsoft.EntityFrameworkCore" },
        { "Banking.Application", "Microsoft.AspNetCore" },
        { "Banking.Application", "Npgsql" },
        { "Banking.Infrastructure", "Banking.Api" },
        { "Banking.Infrastructure", "Microsoft.AspNetCore" },
        { "Banking.Contracts", "Banking.Domain" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Camada_nao_depende_de(string layer, string forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(layer))
            .ShouldNot()
            .HaveDependencyOn(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{layer} depende de {forbidden}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
