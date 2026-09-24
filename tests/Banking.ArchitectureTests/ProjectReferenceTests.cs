using System.Xml.Linq;
using Xunit;

namespace Banking.ArchitectureTests;

/// <summary>
/// Lê os .csproj em vez dos assemblies: uma referência adicionada e ainda não usada já falha aqui.
/// </summary>
public sealed class ProjectReferenceTests
{
    [Fact]
    public void Domain_nao_referencia_projetos_nem_pacotes()
    {
        var project = Load("Banking.Domain");

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    /// <summary>Contracts entra porque os eventos publicados são contrato público, e Contracts não depende de nada.</summary>
    [Fact]
    public void Application_so_referencia_Domain_e_Contracts()
    {
        var project = Load("Banking.Application");

        Assert.Equal(["Banking.Contracts", "Banking.Domain"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    [Fact]
    public void Contracts_nao_referencia_projetos_nem_pacotes()
    {
        var project = Load("Banking.Contracts");

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    [Fact]
    public void Infrastructure_nao_referencia_a_Api()
    {
        var project = Load("Banking.Infrastructure");

        Assert.DoesNotContain("Banking.Api", project.ProjectReferences);
    }

    private static (string[] ProjectReferences, string[] PackageReferences) Load(string projectName)
    {
        var xml = XDocument.Load(RepositoryPaths.ProjectFile(projectName));

        string[] Includes(string item) =>
            [.. xml.Descendants(item).Select(e => (string?)e.Attribute("Include") ?? string.Empty).Order()];

        var projects = Includes("ProjectReference")
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
            .ToArray();

        return (projects, Includes("PackageReference"));
    }
}
