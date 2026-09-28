using System.Diagnostics;
using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Tests;

/// <summary>
/// Repositórios git de verdade numa pasta temporária, e um app que grava tudo nela: as
/// preferências, a memória dos PRs e a limpeza nunca tocam o ~/Library do usuário.
/// </summary>
public sealed class GitSandbox : IDisposable
{
    private readonly List<MainViewModel> _shells = new();

    public GitSandbox()
    {
        // O TMPDIR do macOS é um link (/var → /private/var); o git devolve o caminho real.
        Root = RealPath(Directory.CreateTempSubdirectory("hypercode-tabs-").FullName);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public void Dispose()
    {
        foreach (var shell in _shells)
            foreach (var repository in shell.Repositories.ToList())
                repository.Close();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Um watcher ainda largando a pasta: o TMPDIR limpa depois.
        }
    }

    /// <summary>Repositório com um commit e, opcionalmente, worktrees ao lado dele.</summary>
    public string CreateRepository(string name, params string[] worktrees)
    {
        var path = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        Git(path, "init", "--quiet", "--initial-branch=main");
        Git(path, "-c", "user.name=Teste", "-c", "user.email=teste@exemplo.com", "commit", "--quiet", "--allow-empty", "-m", "inicial");

        foreach (var worktree in worktrees)
            Git(path, "worktree", "add", "--quiet", "-b", worktree, Path.Combine(Root, $"{name}-{worktree}"));

        return path;
    }

    public string CreateFolder(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

    /// <summary>
    /// O app como o usuário o abre, mas gravando na sandbox. Sem <paramref name="settings"/>,
    /// relê o settings.json da sandbox — é o "fechar e abrir o app".
    /// </summary>
    public MainViewModel OpenApp(Settings? settings = null)
    {
        settings ??= File.Exists(SettingsFile) ? SettingsStore.Parse(File.ReadAllText(SettingsFile)) : new Settings();

        // Monitoramento desligado: os repositórios de teste não têm remoto, e o teste não espera tique.
        settings.MonitorProfile ??= "off";

        var shell = new MainViewModel(
            settings,
            saved => File.WriteAllText(SettingsFile, SettingsStore.Serialize(saved)),
            PullRequestMemory.Load(Path.Combine(Root, "pull-requests.json")),
            new AutoCleanupStore(Path.Combine(Root, "autocleanup.json")));

        _shells.Add(shell);
        return shell;
    }

    public Settings SavedSettings() => SettingsStore.Parse(File.ReadAllText(SettingsFile));

    private static void Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
    }

    private static string RealPath(string path)
    {
        var start = new ProcessStartInfo("/bin/pwd", "-P") { WorkingDirectory = path, RedirectStandardOutput = true };
        using var process = Process.Start(start)!;
        var real = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return real.Length > 0 ? real : path;
    }
}
