namespace Hypercode.Services;

/// <summary>
/// Um app aberto pelo Finder herda um PATH mínimo (/usr/bin:/bin:/usr/sbin:/sbin),
/// então git/gh instalados via Homebrew não seriam encontrados. Esta classe reconstrói
/// um PATH razoável e resolve executáveis por caminho absoluto.
/// </summary>
public static class ExecutableLocator
{
    private static readonly string[] CommonDirectories =
    {
        "/opt/homebrew/bin",
        "/opt/homebrew/sbin",
        "/usr/local/bin",
        "/usr/local/sbin",
        "/usr/bin",
        "/bin",
        "/usr/sbin",
        "/sbin",
        "/opt/local/bin",
    };

    public static string BuildSearchPath()
    {
        var directories = new List<string>();

        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            if (!directories.Contains(directory)) directories.Add(directory);
        }

        var inherited = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in inherited.Split(':', StringSplitOptions.RemoveEmptyEntries))
            Add(directory);

        foreach (var directory in CommonDirectories)
            Add(directory);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            Add(Path.Combine(home, ".local", "bin"));
            Add(Path.Combine(home, "bin"));
            Add(Path.Combine(home, ".bun", "bin"));
            Add(Path.Combine(home, ".volta", "bin"));
        }

        return string.Join(':', directories);
    }

    /// <summary>Retorna o caminho absoluto do executável, ou null se não existir.</summary>
    public static string? Find(string executableName)
    {
        if (executableName.Contains('/'))
            return File.Exists(executableName) ? executableName : null;

        foreach (var directory in BuildSearchPath().Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
