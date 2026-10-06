namespace Hypercode.Services;

/// <summary>
/// Um aplicativo de terminal que o app sabe abrir. <paramref name="Id"/> é o que vai no
/// settings.json; <paramref name="Bundles"/>, os nomes do .app a procurar nas pastas de
/// aplicativos; <paramref name="ProcessMarker"/>, o pedaço do caminho do executável que o
/// identifica na árvore do <c>ps</c>. <paramref name="CanFocus"/>: expõe as sessões pelo
/// <c>tty</c> via AppleScript — só assim dá para trazer uma sessão existente para a frente.
/// </summary>
public sealed record TerminalDefinition(
    string Id,
    string Name,
    TerminalApp App,
    string BundleId,
    IReadOnlyList<string> Bundles,
    string ProcessMarker,
    bool CanFocus);

/// <summary>
/// O terminal que vale agora: a escolha das configurações, se instalada, ou o padrão do
/// sistema. <paramref name="Notice"/> diz quando a escolha sumiu e o padrão entrou no lugar.
/// </summary>
public sealed record TerminalChoice(TerminalDefinition Definition, bool IsSystemDefault, string? Notice);

/// <summary>
/// Abre uma janela nova do terminal escolhido nas configurações na pasta indicada e roda um
/// comando (ou só abre o shell, se o comando vier vazio), com um título opcional. O padrão do
/// sistema é o iTerm2 se instalado e, senão, o Terminal.app.
/// </summary>
public static class TerminalLauncher
{
    /// <summary>O valor do settings.json para "Padrão do sistema" — o mesmo que não ter nada.</summary>
    public const string SystemDefaultId = "system";

    public static readonly TerminalDefinition ITerm2 = new(
        "iterm2", "iTerm2", TerminalApp.ITerm2, "com.googlecode.iterm2", new[] { "iTerm.app", "iTerm2.app" }, "/iTerm", CanFocus: true);

    public static readonly TerminalDefinition AppleTerminal = new(
        "terminal", "Terminal", TerminalApp.Terminal, "com.apple.Terminal", new[] { "Terminal.app" }, "/Terminal.app/", CanFocus: true);

    public static readonly TerminalDefinition Ghostty = new(
        "ghostty", "Ghostty", TerminalApp.Ghostty, "com.mitchellh.ghostty", new[] { "Ghostty.app" }, "/Ghostty.app/", CanFocus: false);

    public static readonly TerminalDefinition WezTerm = new(
        "wezterm", "WezTerm", TerminalApp.WezTerm, "com.github.wez.wezterm", new[] { "WezTerm.app" }, "/WezTerm.app/", CanFocus: false);

    public static readonly TerminalDefinition Alacritty = new(
        "alacritty", "Alacritty", TerminalApp.Alacritty, "org.alacritty", new[] { "Alacritty.app" }, "/Alacritty.app/", CanFocus: false);

    public static readonly TerminalDefinition Kitty = new(
        "kitty", "kitty", TerminalApp.Kitty, "net.kovidgoyal.kitty", new[] { "kitty.app" }, "/kitty.app/", CanFocus: false);

    /// <summary>Os terminais conhecidos, na ordem do seletor.</summary>
    public static readonly IReadOnlyList<TerminalDefinition> Known = new[] { ITerm2, AppleTerminal, Ghostty, WezTerm, Alacritty, Kitty };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> InstalledLocations = new(FindInstalled);

    private static TerminalChoice _current = Resolve(null, IsInstalled);

    /// <summary>Onde o .app de cada terminal instalado está, pelo id. Lido uma vez, na primeira consulta.</summary>
    public static IReadOnlyDictionary<string, string> Installed => InstalledLocations.Value;

    public static bool IsInstalled(TerminalDefinition terminal) => Installed.ContainsKey(terminal.Id);

    public static bool IsITerm2Available => IsInstalled(ITerm2);

    /// <summary>O terminal que o lançamento usa agora.</summary>
    public static TerminalChoice Current => _current;

    /// <summary>Nome do terminal que será usado — serve para mensagens na interface.</summary>
    public static string TerminalName => _current.Definition.Name;

    /// <summary>Passa a usar a escolha das configurações (null ou "system": o padrão do sistema).</summary>
    public static TerminalChoice Configure(string? preference) => _current = Resolve(preference, IsInstalled);

    /// <summary>
    /// O terminal de uma preferência. Desconhecida ou não instalada volta ao padrão do sistema,
    /// com aviso — a preferência não é apagada: reinstalado o app, ela volta a valer.
    /// </summary>
    internal static TerminalChoice Resolve(string? preference, Func<TerminalDefinition, bool> isInstalled)
    {
        var fallback = isInstalled(ITerm2) ? ITerm2 : AppleTerminal;
        if (string.IsNullOrWhiteSpace(preference) || preference.Equals(SystemDefaultId, StringComparison.OrdinalIgnoreCase))
            return new TerminalChoice(fallback, IsSystemDefault: true, Notice: null);

        var chosen = Find(preference);
        if (chosen is not null && isInstalled(chosen)) return new TerminalChoice(chosen, IsSystemDefault: false, Notice: null);

        var name = chosen?.Name ?? preference.Trim();
        return new TerminalChoice(
            fallback,
            IsSystemDefault: true,
            $"O {name} escolhido nas configurações não está instalado; usando o padrão do sistema, o {fallback.Name}.");
    }

    public static TerminalDefinition? Find(string? id)
        => Known.FirstOrDefault(terminal => string.Equals(terminal.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static async Task LaunchAsync(
        string directory,
        string? command,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"A pasta '{directory}' não existe mais.");

        var terminal = _current.Definition;
        var shellCommand = BuildShellCommand(directory, command, terminal.Name);

        var (fileName, arguments) = terminal.App switch
        {
            TerminalApp.ITerm2 => ("/usr/bin/osascript", (IReadOnlyList<string>)new[] { "-e", BuildITerm2Script(shellCommand, title) }),
            TerminalApp.Terminal => ("/usr/bin/osascript", new[] { "-e", BuildTerminalAppScript(shellCommand, title) }),
            _ => ("/usr/bin/open", BuildOpenArguments(terminal, Installed.GetValueOrDefault(terminal.Id) ?? terminal.Bundles[0], directory, shellCommand, title, LoginShell())),
        };

        var result = await ProcessRunner.RunAsync(fileName, arguments, null, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success) throw new InvalidOperationException(DescribeFailure(terminal, result.FirstErrorLine));
    }

    /// <summary>
    /// A mensagem de falha do lançamento. A recusa de automação (-1743) é a que mais aparece
    /// e a que menos se explica sozinha: ganha o caminho dos Ajustes.
    /// </summary>
    internal static string DescribeFailure(TerminalDefinition terminal, string error)
        => error.Contains("-1743", StringComparison.Ordinal)
            ? $"Não consegui abrir o {terminal.Name}: o Hypercode não tem permissão para controlá-lo. Autorize em Ajustes do Sistema → Privacidade e Segurança → Automação."
            : $"Não consegui abrir o {terminal.Name}: {error}";

    /// <summary>Abre a pasta no Finder.</summary>
    public static Task RevealInFinderAsync(string directory, CancellationToken cancellationToken = default)
        => ProcessRunner.RunAsync("/usr/bin/open", new[] { directory }, null, TimeSpan.FromSeconds(10), cancellationToken: cancellationToken);

    public static Task OpenUrlAsync(string url, CancellationToken cancellationToken = default)
        => ProcessRunner.RunAsync("/usr/bin/open", new[] { url }, null, TimeSpan.FromSeconds(10), cancellationToken: cancellationToken);

    /// <summary>
    /// Variáveis que o Claude Code usa para marcar uma sessão como filha de outra. O iTerm2 as
    /// herda quando é aberto de dentro de uma sessão do Claude e as repassa a todo shell novo, o que
    /// faz o `claude` aberto por aqui não gravar transcript (e sumir do `--resume`).
    /// </summary>
    private static readonly string[] InheritedSessionVariables =
    {
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_ENTRYPOINT",
    };

    /// <summary>
    /// `unset` das variáveis de sessão herdadas, `cd` na pasta e, havendo comando, ele. Se o
    /// `cd` falha — o caso típico é o terminal sem a permissão de Volumes removíveis do macOS
    /// para um disco externo —, o terminal diz isso em vez de ficar parado num shell inútil.
    /// </summary>
    internal static string BuildShellCommand(string directory, string? command, string terminalName = "terminal")
    {
        var unset = $"unset {string.Join(' ', InheritedSessionVariables)}";
        var cd = $"cd {QuoteForShell(directory)}";
        var explain = $"echo {QuoteForShell(CdFailureMessage(directory, terminalName))}";

        return string.IsNullOrWhiteSpace(command)
            ? $"{unset}; {cd} || {explain}"
            : $"{unset}; if {cd}; then {command.Trim()}; else {explain}; fi";
    }

    /// <summary>O que o terminal mostra quando não consegue entrar na pasta.</summary>
    internal static string CdFailureMessage(string directory, string terminalName)
        => directory.StartsWith("/Volumes/", StringComparison.Ordinal)
            ? $"Hypercode: o {terminalName} não conseguiu entrar em {directory}. Num volume externo, o macOS pede autorização por app: Ajustes do Sistema → Privacidade e Segurança → Arquivos e Pastas → {terminalName} → Volumes removíveis."
            : $"Hypercode: o {terminalName} não conseguiu entrar em {directory}.";

    // A referência por bundle id evita a ambiguidade entre os nomes "iTerm" e "iTerm2".
    internal static string BuildITerm2Script(string shellCommand, string? title = null)
    {
        var setTitle = string.IsNullOrWhiteSpace(title)
            ? ""
            : $"set name to \"{EscapeForAppleScript(title)}\"";

        return $"""
                tell application id "{ITerm2.BundleId}"
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

    /// <summary>
    /// Os argumentos do <c>open</c> para os terminais sem AppleScript: instância nova (-n) do
    /// .app, que recebe o shell do usuário rodando o comando e, depois dele, um shell de login
    /// interativo — a janela não fecha quando o comando termina, como no iTerm2. O <c>-i</c>
    /// lê o .zshrc, onde costuma estar o PATH do claude.
    /// </summary>
    internal static IReadOnlyList<string> BuildOpenArguments(
        TerminalDefinition terminal,
        string appPath,
        string directory,
        string shellCommand,
        string? title,
        string shell)
    {
        var script = $"{shellCommand}; exec {QuoteForShell(shell)} -l";
        var arguments = new List<string> { "-n", "-a", appPath, "--args" };
        var hasTitle = !string.IsNullOrWhiteSpace(title);

        switch (terminal.App)
        {
            case TerminalApp.Ghostty:
                arguments.Add($"--working-directory={directory}");
                if (hasTitle) arguments.Add($"--title={title}");
                arguments.AddRange(new[] { "-e", shell, "-lic", script });
                break;
            case TerminalApp.WezTerm:
                arguments.AddRange(new[] { "start", "--cwd", directory, "--", shell, "-lic", script });
                break;
            case TerminalApp.Alacritty:
                arguments.AddRange(new[] { "--working-directory", directory });
                if (hasTitle) arguments.AddRange(new[] { "--title", title! });
                arguments.AddRange(new[] { "-e", shell, "-lic", script });
                break;
            case TerminalApp.Kitty:
                arguments.AddRange(new[] { "--directory", directory });
                if (hasTitle) arguments.AddRange(new[] { "--title", title! });
                arguments.AddRange(new[] { shell, "-lic", script });
                break;
            default:
                throw new InvalidOperationException($"O {terminal.Name} não abre pelo open.");
        }

        return arguments;
    }

    /// <summary>O shell de login do usuário; o zsh, padrão do macOS, se o ambiente não disser.</summary>
    private static string LoginShell()
        => Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell && shell.StartsWith('/') ? shell : "/bin/zsh";

    private static IReadOnlyDictionary<string, string> FindInstalled()
    {
        var folders = new List<string> { "/Applications", "/Applications/Utilities", "/System/Applications/Utilities" };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) folders.Add(Path.Combine(home, "Applications"));

        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var terminal in Known)
        {
            var location = folders
                .SelectMany(folder => terminal.Bundles.Select(bundle => Path.Combine(folder, bundle)))
                .FirstOrDefault(Directory.Exists);
            if (location is not null) installed[terminal.Id] = location;
        }

        return installed;
    }

    /// <summary>Envolve em aspas simples, escapando aspas simples internas: it's -> 'it'\''s'</summary>
    internal static string QuoteForShell(string value)
        => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Escapa barras invertidas e aspas duplas para caber num literal de string do AppleScript.</summary>
    internal static string EscapeForAppleScript(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
