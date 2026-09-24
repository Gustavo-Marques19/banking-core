namespace Banking.IntegrationTests;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string PostgresInitScript => Path.Combine(Root, "infra", "postgres", "init", "01-roles.sh");

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
