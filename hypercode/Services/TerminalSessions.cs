namespace Hypercode.Services;

/// <summary>O app dono de uma sessão de terminal — só o iTerm2 e o Terminal.app sabem dar foco nela.</summary>
public enum TerminalApp
{
    ITerm2,
    Terminal,
    Other,
}

/// <summary>
/// Uma sessão de terminal com processo dentro de um worktree: o <c>tty</c>, o app dono, há
/// quanto tempo a sessão existe (o processo mais antigo daquele <c>tty</c>) e, no iTerm2, o
/// nome da sessão — o título que ele mostra na aba.
/// </summary>
public sealed record TerminalSession(string Tty, TerminalApp App, TimeSpan Age, string? Name)
{
    public bool CanFocus => App is not TerminalApp.Other;

    public string AppName => App switch
    {
        TerminalApp.ITerm2 => "iTerm2",
        TerminalApp.Terminal => "Terminal",
        _ => "outro terminal",
    };
}

/// <summary>
/// O que há aberto dentro de um worktree: as sessões de terminal, da mais recente para a mais
/// antiga, e o nome dos processos sem terminal com a pasta de trabalho ali (um
/// <c>dotnet watch</c>: a pasta está em uso, mas não há sessão para onde ir). Os processos de
/// dentro das sessões — o claude e os servidores MCP dele — ficam de fora: seriam ruído.
/// </summary>
public sealed record TerminalPresence(IReadOnlyList<TerminalSession> Sessions, IReadOnlyList<string> Processes)
{
    public static readonly TerminalPresence None = new(Array.Empty<TerminalSession>(), Array.Empty<string>());

    public bool IsEmpty => Sessions.Count == 0 && Processes.Count == 0;

    /// <summary>A sessão mais recente a que dá para dar foco, se houver.</summary>
    public TerminalSession? Focusable => Sessions.FirstOrDefault(session => session.CanFocus);

    public bool Equals(TerminalPresence? other)
        => other is not null && Sessions.SequenceEqual(other.Sessions) && Processes.SequenceEqual(other.Processes);

    public override int GetHashCode() => HashCode.Combine(Sessions.Count, Processes.Count);
}

/// <summary>Uma linha do <c>ps</c>: o <c>tty</c> é null para processo sem terminal (o <c>??</c>).</summary>
public sealed record ProcessEntry(int Pid, int ParentPid, string? Tty, TimeSpan Age, string Command);

/// <summary>
/// Que worktree tem terminal aberto, e como trazer esse terminal para a frente. A pasta de
/// trabalho de cada processo vem do <c>lsof -d cwd</c> — o mesmo da limpeza automática, uma
/// chamada para todos os worktrees —; o <c>tty</c> e a ascendência, do <c>ps</c>, que diz
/// também de que app é a sessão sem precisar de AppleScript (e da permissão de automação).
/// O AppleScript fica para o nome das sessões do iTerm2 e para o foco.
/// </summary>
public static class TerminalSessions
{
    private const string ITerm2BundleId = "com.googlecode.iterm2";

    /// <summary>
    /// O que há aberto em cada worktree, pelo caminho. Worktree sem nada não entra no
    /// dicionário. Null se o <c>lsof</c> ou o <c>ps</c> falhou: é "não sei", não "não há".
    /// Com <paramref name="withNames"/>, pergunta ao iTerm2 o nome das sessões dele.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, TerminalPresence>?> DetectAsync(
        IReadOnlyList<string> worktreePaths,
        bool withNames,
        CancellationToken cancellationToken = default)
    {
        if (worktreePaths.Count == 0) return new Dictionary<string, TerminalPresence>();

        var directoriesTask = ListProcessDirectoriesAsync(cancellationToken);
        var processesTask = ListProcessesAsync(cancellationToken);
        await Task.WhenAll(directoriesTask, processesTask).ConfigureAwait(false);

        if (directoriesTask.Result is not { } directories || processesTask.Result is not { } processes) return null;

        var presence = Assign(worktreePaths, directories, processes, Environment.ProcessId);
        if (!withNames || !presence.Values.Any(item => item.Sessions.Any(session => session.App is TerminalApp.ITerm2)))
            return presence;

        // O nome é enfeite do tooltip: sem ele, a detecção continua valendo.
        var names = await ListITerm2NamesAsync(cancellationToken).ConfigureAwait(false);
        if (names.Count == 0) return presence;

        return presence.ToDictionary(
            item => item.Key,
            item => item.Value with
            {
                Sessions = item.Value.Sessions
                    .Select(session => names.TryGetValue(session.Tty, out var name) ? session with { Name = name } : session)
                    .ToList(),
            },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Traz a sessão para a frente: janela, aba e sessão selecionadas, e o app ativado.
    /// Devolve false se a sessão não foi achada — fechou nesse meio-tempo — ou se o app não é
    /// um dos que sabem fazer isso.
    /// </summary>
    public static async Task<bool> FocusAsync(TerminalSession session, CancellationToken cancellationToken = default)
    {
        var script = session.App switch
        {
            TerminalApp.ITerm2 => BuildITerm2FocusScript(session.Tty),
            TerminalApp.Terminal => BuildTerminalAppFocusScript(session.Tty),
            _ => null,
        };
        if (script is null) return false;

        var result = await ProcessRunner.RunAsync(
            "/usr/bin/osascript",
            new[] { "-e", script },
            null,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException($"Não consegui ir para o {session.AppName}: {result.FirstErrorLine}");

        return result.StandardOutput.Trim() == "ok";
    }

    /// <summary>
    /// Distribui os processos pelos worktrees. Cada processo fica com o worktree mais fundo que
    /// o contém: com worktree aninhado em outro, o terminal do de dentro não acende o de fora.
    /// O próprio app e os filhos dele (o lsof, o ps) ficam de fora.
    /// </summary>
    internal static Dictionary<string, TerminalPresence> Assign(
        IReadOnlyList<string> worktreePaths,
        IReadOnlyList<(int Pid, string Directory)> directories,
        IReadOnlyList<ProcessEntry> processes,
        int selfPid)
    {
        var roots = worktreePaths
            .Select(path => (Path: path, Root: path.TrimEnd('/')))
            .OrderByDescending(item => item.Root.Length)
            .ToList();

        var byPid = new Dictionary<int, ProcessEntry>();
        foreach (var process in processes) byPid[process.Pid] = process;

        // A sessão tem a idade do processo mais antigo do tty (o login ou o shell), e o app vem de
        // qualquer processo dele: shell órfão, com o pai já morto, não diz de quem é.
        var sessionAge = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var sessionApp = new Dictionary<string, TerminalApp>(StringComparer.Ordinal);
        foreach (var process in processes)
        {
            if (process.Tty is not { } tty) continue;
            if (!sessionAge.TryGetValue(tty, out var age) || process.Age > age) sessionAge[tty] = process.Age;
            if (sessionApp.GetValueOrDefault(tty, TerminalApp.Other) is TerminalApp.Other)
                sessionApp[tty] = AppOf(process, byPid);
        }

        var owned = new Dictionary<string, List<ProcessEntry>>(StringComparer.Ordinal);
        foreach (var (pid, directory) in directories)
        {
            if (!byPid.TryGetValue(pid, out var process) || pid == selfPid || process.ParentPid == selfPid) continue;

            var cwd = directory.TrimEnd('/');
            var owner = roots.FirstOrDefault(item =>
                string.Equals(cwd, item.Root, StringComparison.OrdinalIgnoreCase)
                || cwd.StartsWith(item.Root + "/", StringComparison.OrdinalIgnoreCase));
            if (owner.Path is null) continue;

            if (!owned.TryGetValue(owner.Path, out var list)) owned[owner.Path] = list = new List<ProcessEntry>();
            list.Add(process);
        }

        return owned.ToDictionary(
            item => item.Key,
            item => new TerminalPresence(
                item.Value
                    .Select(process => process.Tty)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Select(tty => new TerminalSession(tty, sessionApp.GetValueOrDefault(tty, TerminalApp.Other), sessionAge[tty], null))
                    .OrderBy(session => session.Age)
                    .ThenBy(session => session.Tty, StringComparer.Ordinal)
                    .ToList(),
                item.Value
                    .Where(process => process.Tty is null)
                    .Select(process => ProcessName(process.Command))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Sobe pelos pais até achar o app: no iTerm2 o shell é filho do login, que é filho do
    /// iTermServer (em ~/Library/Application Support/iTerm2), que é filho do iTerm2; no
    /// Terminal.app o login é filho direto dele.
    /// </summary>
    internal static TerminalApp AppOf(ProcessEntry process, IReadOnlyDictionary<int, ProcessEntry> byPid)
    {
        var current = process;
        for (var step = 0; step < 64 && current is not null; step++)
        {
            if (current.Command.Contains("/iTerm", StringComparison.Ordinal)) return TerminalApp.ITerm2;
            if (current.Command.Contains("/Terminal.app/", StringComparison.Ordinal)) return TerminalApp.Terminal;
            if (current.ParentPid <= 1) break;
            current = byPid.GetValueOrDefault(current.ParentPid);
        }

        return TerminalApp.Other;
    }

    /// <summary>O nome curto do comando: <c>/usr/bin/login</c> vira <c>login</c>, e o <c>-zsh</c> de login vira <c>zsh</c>.</summary>
    internal static string ProcessName(string command)
    {
        var name = command.StartsWith('/') ? Path.GetFileName(command) : command.Split(' ', 2)[0];
        return name.TrimStart('-');
    }

    /// <summary>Saída do <c>lsof -F pn</c>: uma linha <c>p&lt;pid&gt;</c> e, abaixo, a <c>n&lt;pasta&gt;</c> dela.</summary>
    internal static List<(int Pid, string Directory)> ParseLsof(string output)
    {
        var entries = new List<(int, string)>();
        int? pid = null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line[0] == 'p') pid = int.TryParse(line.AsSpan(1), out var parsed) ? parsed : null;
            else if (line[0] == 'n' && pid is { } current) entries.Add((current, line[1..]));
        }

        return entries;
    }

    /// <summary>Saída do <c>ps -o pid=,ppid=,tty=,etime=,comm=</c>. O comando vai até o fim da linha: pode ter espaço.</summary>
    internal static List<ProcessEntry> ParsePs(string output)
    {
        var entries = new List<ProcessEntry>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split((char[]?)null, 5, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5
                || !int.TryParse(fields[0], out var pid)
                || !int.TryParse(fields[1], out var parentPid)
                || ParseElapsed(fields[3]) is not { } age)
                continue;

            var tty = fields[2] is "??" or "-" ? null : $"/dev/{fields[2]}";
            entries.Add(new ProcessEntry(pid, parentPid, tty, age, fields[4].Trim()));
        }

        return entries;
    }

    /// <summary>O <c>etime</c> do ps: <c>[[dd-]hh:]mm:ss</c>.</summary>
    internal static TimeSpan? ParseElapsed(string value)
    {
        var days = 0;
        var clock = value;
        var dash = value.IndexOf('-');
        if (dash > 0)
        {
            if (!int.TryParse(value.AsSpan(0, dash), out days)) return null;
            clock = value[(dash + 1)..];
        }

        var parts = clock.Split(':');
        if (parts.Length is < 2 or > 3) return null;

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i])) return null;

        var (hours, minutes, seconds) = numbers.Length == 3 ? (numbers[0], numbers[1], numbers[2]) : (0, numbers[0], numbers[1]);
        return new TimeSpan(days, hours, minutes, seconds);
    }

    /// <summary>Saída do script de nomes: <c>tty</c>, tab e o nome da sessão, uma por linha.</summary>
    internal static Dictionary<string, string> ParseSessionNames(string output)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t', 2);
            if (fields.Length == 2 && fields[0].StartsWith("/dev/", StringComparison.Ordinal) && fields[1].Trim() is { Length: > 0 } name)
                names[fields[0]] = name;
        }

        return names;
    }

    // "tab" dentro do tell do iTerm2 é a classe de aba, não o caractere: o separador vem de fora.
    internal static string BuildITerm2NamesScript()
        => $"""
            set separator to character id 9
            set output to ""
            if application id "{ITerm2BundleId}" is running then
                tell application id "{ITerm2BundleId}"
                    repeat with w in windows
                        repeat with t in tabs of w
                            repeat with s in sessions of t
                                set output to output & (tty of s) & separator & (name of s) & linefeed
                            end repeat
                        end repeat
                    end repeat
                end tell
            end if
            return output
            """;

    internal static string BuildITerm2FocusScript(string tty)
        => $"""
            tell application id "{ITerm2BundleId}"
                repeat with w in windows
                    repeat with t in tabs of w
                        repeat with s in sessions of t
                            if tty of s is "{TerminalLauncher.EscapeForAppleScript(tty)}" then
                                select w
                                tell t to select
                                tell s to select
                                activate
                                return "ok"
                            end if
                        end repeat
                    end repeat
                end repeat
            end tell
            return ""
            """;

    internal static string BuildTerminalAppFocusScript(string tty)
        => $"""
            tell application "Terminal"
                repeat with w in windows
                    repeat with t in tabs of w
                        if tty of t is "{TerminalLauncher.EscapeForAppleScript(tty)}" then
                            set selected tab of w to t
                            set index of w to 1
                            activate
                            return "ok"
                        end if
                    end repeat
                end repeat
            end tell
            return ""
            """;

    private static async Task<IReadOnlyList<(int Pid, string Directory)>?> ListProcessDirectoriesAsync(CancellationToken cancellationToken)
    {
        var lsof = ExecutableLocator.Find("lsof") ?? (File.Exists("/usr/sbin/lsof") ? "/usr/sbin/lsof" : null);
        if (lsof is null) return null;

        try
        {
            var result = await ProcessRunner.RunAsync(
                lsof,
                new[] { "-n", "-P", "-w", "-d", "cwd", "-Fpn" },
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // O lsof sai com 1 quando algum processo não pôde ser lido, mesmo listando os demais.
            return result.StandardOutput.Length == 0 ? null : ParseLsof(result.StandardOutput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<ProcessEntry>?> ListProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "/bin/ps",
                new[] { "-A", "-o", "pid=,ppid=,tty=,etime=,comm=" },
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result.Success ? ParsePs(result.StandardOutput) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<Dictionary<string, string>> ListITerm2NamesAsync(CancellationToken cancellationToken)
    {
        if (!TerminalLauncher.IsITerm2Available) return new Dictionary<string, string>();

        try
        {
            var result = await ProcessRunner.RunAsync(
                "/usr/bin/osascript",
                new[] { "-e", BuildITerm2NamesScript() },
                null,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            return result.Success ? ParseSessionNames(result.StandardOutput) : new Dictionary<string, string>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<string, string>();
        }
    }
}
