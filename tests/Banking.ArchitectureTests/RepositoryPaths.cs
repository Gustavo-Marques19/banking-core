namespace Banking.ArchitectureTests;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string ProjectFile(string projectName) =>
        Path.Combine(Root, "src", projectName, $"{projectName}.csproj");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Banking.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Banking.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}
