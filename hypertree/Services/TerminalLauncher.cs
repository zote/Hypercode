namespace Hypertree.Services;

/// <summary>
/// Abre uma nova janela do iTerm2 na pasta indicada e roda um comando.
/// Se o iTerm2 não estiver instalado, cai para o Terminal.app.
/// </summary>
public static class TerminalLauncher
{
    private const string ITerm2BundleId = "com.googlecode.iterm2";

    private static readonly Lazy<string?> ITerm2Location = new(FindITerm2);

    public static bool IsITerm2Available => ITerm2Location.Value is not null;

    /// <summary>Nome do terminal que será usado — serve para mensagens na interface.</summary>
    public static string TerminalName => IsITerm2Available ? "iTerm2" : "Terminal";

    public static async Task LaunchAsync(
        string directory,
        string command,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"A pasta '{directory}' não existe mais.");

        var shellCommand = $"cd {QuoteForShell(directory)} && {command}";

        var script = IsITerm2Available
            ? BuildITerm2Script(shellCommand)
            : BuildTerminalAppScript(shellCommand);

        var result = await ProcessRunner.RunAsync(
            "/usr/bin/osascript",
            new[] { "-e", script },
            null,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException($"Não consegui abrir o {TerminalName}: {result.FirstErrorLine}");
    }

    /// <summary>Abre a pasta no Finder.</summary>
    public static Task RevealInFinderAsync(string directory, CancellationToken cancellationToken = default)
        => ProcessRunner.RunAsync("/usr/bin/open", new[] { directory }, null, TimeSpan.FromSeconds(10), cancellationToken);

    public static Task OpenUrlAsync(string url, CancellationToken cancellationToken = default)
        => ProcessRunner.RunAsync("/usr/bin/open", new[] { url }, null, TimeSpan.FromSeconds(10), cancellationToken);

    // A referência por bundle id evita a ambiguidade entre os nomes "iTerm" e "iTerm2".
    internal static string BuildITerm2Script(string shellCommand) =>
        $"""
         tell application id "{ITerm2BundleId}"
             set targetWindow to (create window with default profile)
             tell current session of targetWindow
                 write text "{EscapeForAppleScript(shellCommand)}"
             end tell
             activate
         end tell
         """;

    internal static string BuildTerminalAppScript(string shellCommand) =>
        $"""
         tell application "Terminal"
             do script "{EscapeForAppleScript(shellCommand)}"
             activate
         end tell
         """;

    private static string? FindITerm2()
    {
        var candidates = new List<string>
        {
            "/Applications/iTerm.app",
            "/Applications/iTerm2.app",
        };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(home, "Applications", "iTerm.app"));
            candidates.Add(Path.Combine(home, "Applications", "iTerm2.app"));
        }

        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Envolve em aspas simples, escapando aspas simples internas: it's -> 'it'\''s'</summary>
    internal static string QuoteForShell(string value)
        => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Escapa barras invertidas e aspas duplas para caber num literal de string do AppleScript.</summary>
    internal static string EscapeForAppleScript(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
