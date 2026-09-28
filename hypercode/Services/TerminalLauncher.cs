namespace Hypercode.Services;

/// <summary>
/// Abre uma nova janela do iTerm2 na pasta indicada e roda um comando (ou só abre o shell,
/// se o comando vier vazio), com um título opcional na sessão/aba. Se o iTerm2 não estiver
/// instalado, cai para o Terminal.app.
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
        string? command,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"A pasta '{directory}' não existe mais.");

        var shellCommand = BuildShellCommand(directory, command);

        var script = IsITerm2Available
            ? BuildITerm2Script(shellCommand, title)
            : BuildTerminalAppScript(shellCommand, title);

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

    /// <summary>`cd` na pasta e, havendo comando, `&amp;&amp; comando`.</summary>
    internal static string BuildShellCommand(string directory, string? command)
        => string.IsNullOrWhiteSpace(command)
            ? $"cd {QuoteForShell(directory)}"
            : $"cd {QuoteForShell(directory)} && {command.Trim()}";

    // A referência por bundle id evita a ambiguidade entre os nomes "iTerm" e "iTerm2".
    internal static string BuildITerm2Script(string shellCommand, string? title = null)
    {
        var setTitle = string.IsNullOrWhiteSpace(title)
            ? ""
            : $"set name to \"{EscapeForAppleScript(title)}\"";

        return $"""
                tell application id "{ITerm2BundleId}"
                    set targetWindow to (create window with default profile)
                    tell current session of targetWindow
                        {setTitle}
                        write text "{EscapeForAppleScript(shellCommand)}"
                    end tell
                    activate
                end tell
                """;
    }

    // "custom title" é o título da aba que o Terminal.app mostra no lugar do automático.
    internal static string BuildTerminalAppScript(string shellCommand, string? title = null)
    {
        var setTitle = string.IsNullOrWhiteSpace(title)
            ? ""
            : $"set custom title of targetTab to \"{EscapeForAppleScript(title)}\"";

        return $"""
                tell application "Terminal"
                    set targetTab to do script "{EscapeForAppleScript(shellCommand)}"
                    {setTitle}
                    activate
                end tell
                """;
    }

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
