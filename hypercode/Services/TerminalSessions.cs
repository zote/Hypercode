using System.Text.RegularExpressions;

namespace Hypercode.Services;

/// <summary>
/// O app dono de uma sessão de terminal — só o iTerm2 e o Terminal.app sabem dar foco nela.
/// <see cref="Multiplexer"/> é sessão de zmx, tmux, screen ou zellij: tem <c>tty</c>, mas não
/// é janela nenhuma — pode estar desanexada, ou dentro de outro app (o zmx do supacode).
/// </summary>
public enum TerminalApp
{
    ITerm2,
    Terminal,
    Multiplexer,
    Other,
}

/// <summary>
/// Uma sessão de terminal com processo dentro de um worktree: o <c>tty</c>, o app dono, há
/// quanto tempo a sessão existe (o processo mais antigo daquele <c>tty</c>) e, no iTerm2, o
/// nome da sessão — o título que ele mostra na aba. Numa sessão de multiplexador,
/// <paramref name="Multiplexer"/> diz qual: <c>tmux</c>, ou <c>supacode (zmx)</c>.
/// </summary>
public sealed record TerminalSession(string Tty, TerminalApp App, TimeSpan Age, string? Name, string? Multiplexer = null)
{
    public bool CanFocus => App is TerminalApp.ITerm2 or TerminalApp.Terminal;

    public string AppName => App switch
    {
        TerminalApp.ITerm2 => "iTerm2",
        TerminalApp.Terminal => "Terminal",
        TerminalApp.Multiplexer => Multiplexer ?? "multiplexador",
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
        return withNames ? await NameITerm2SessionsAsync(presence, cancellationToken).ConfigureAwait(false) : presence;
    }

    /// <summary>
    /// Pergunta ao iTerm2 o nome das sessões dele — o pedaço mais caro da detecção, por isso
    /// separado: o tique periódico só chama quando aparece sessão que ainda não tem nome.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, TerminalPresence>> NameITerm2SessionsAsync(
        IReadOnlyDictionary<string, TerminalPresence> presence,
        CancellationToken cancellationToken = default)
    {
        if (!presence.Values.Any(item => item.Sessions.Any(session => session.App is TerminalApp.ITerm2))) return presence;

        // O nome é enfeite do tooltip: sem ele, a detecção continua valendo.
        var names = await ListITerm2NamesAsync(cancellationToken).ConfigureAwait(false);
        return ApplyNames(presence, names);
    }

    /// <summary>
    /// Os <c>tty</c> com algum processo vivo agora, pelo <c>ps -A -o tty=</c>. Null se o ps
    /// falhou: é "não sei", e nenhuma sessão deve ser dada por fechada.
    /// </summary>
    public static async Task<IReadOnlySet<string>?> ListLiveTtysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "/bin/ps",
                new[] { "-A", "-o", "tty=" },
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result.Success && result.StandardOutput.Length > 0 ? ParseTtys(result.StandardOutput) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tira da presença as sessões cujo <c>tty</c> não tem mais processo nenhum: o terminal
    /// fechou. Os processos sem terminal ficam — o ps de tty não diz nada sobre eles. Sem
    /// nada a tirar, devolve a mesma instância.
    /// </summary>
    public static TerminalPresence Prune(TerminalPresence presence, IReadOnlySet<string> liveTtys)
    {
        if (presence.Sessions.All(session => liveTtys.Contains(session.Tty))) return presence;

        return presence with { Sessions = presence.Sessions.Where(session => liveTtys.Contains(session.Tty)).ToList() };
    }

    /// <summary>
    /// Leva para a leitura nova os nomes da anterior, sessão a sessão, sem perguntar ao iTerm2.
    /// A mesma sessão tem o mesmo <c>tty</c> e app, e a idade dela só cresce; um <c>tty</c>
    /// reaproveitado por sessão nova vem mais novo e fica sem nome. <paramref name="hasNewITerm2Session"/>
    /// diz se sobrou sessão do iTerm2 que a leitura anterior não conhecia — só aí vale o AppleScript.
    /// </summary>
    public static IReadOnlyDictionary<string, TerminalPresence> CarryNames(
        IReadOnlyDictionary<string, TerminalPresence> presence,
        IEnumerable<TerminalSession> previous,
        out bool hasNewITerm2Session)
    {
        var known = new Dictionary<string, TerminalSession>(StringComparer.Ordinal);
        foreach (var session in previous) known.TryAdd(session.Tty, session);

        var isNew = false;
        TerminalSession Carry(TerminalSession session)
        {
            if (known.TryGetValue(session.Tty, out var before) && before.App == session.App && session.Age >= before.Age)
                return before.Name is null ? session : session with { Name = before.Name };

            if (session.App is TerminalApp.ITerm2) isNew = true;
            return session;
        }

        var carried = presence.ToDictionary(
            item => item.Key,
            item => item.Value with { Sessions = item.Value.Sessions.Select(Carry).ToList() },
            StringComparer.Ordinal);

        hasNewITerm2Session = isNew;
        return carried;
    }

    /// <summary>Dá a cada sessão o nome do <c>tty</c> dela, quando há; as demais ficam como estão.</summary>
    private static IReadOnlyDictionary<string, TerminalPresence> ApplyNames(
        IReadOnlyDictionary<string, TerminalPresence> presence,
        IReadOnlyDictionary<string, string> names)
    {
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
        var sessionApp = new Dictionary<string, (TerminalApp App, string? Multiplexer)>(StringComparer.Ordinal);
        foreach (var process in processes)
        {
            if (process.Tty is not { } tty) continue;
            if (!sessionAge.TryGetValue(tty, out var age) || process.Age > age) sessionAge[tty] = process.Age;
            if (!sessionApp.TryGetValue(tty, out var known) || known.App is TerminalApp.Other)
                sessionApp[tty] = (AppOf(process, byPid, out var multiplexer), multiplexer);
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

        TerminalPresence PresenceOf(List<ProcessEntry> owned)
        {
            var sessions = owned
                .Select(process => process.Tty)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(tty => sessionApp.TryGetValue(tty, out var app)
                    ? new TerminalSession(tty, app.App, sessionAge[tty], null, app.Multiplexer)
                    : new TerminalSession(tty, TerminalApp.Other, sessionAge[tty], null))
                .OrderBy(session => session.Age)
                .ThenBy(session => session.Tty, StringComparer.Ordinal)
                .ToList();

            // O servidor do multiplexador não tem tty, mas é a própria sessão, já listada: seria ruído.
            var hasMultiplexer = sessions.Any(session => session.App is TerminalApp.Multiplexer);

            return new TerminalPresence(
                sessions,
                owned
                    .Where(process => process.Tty is null && !(hasMultiplexer && MultiplexerOf(process.Command) is not null))
                    .Select(process => ProcessName(process.Command))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList());
        }

        return owned.ToDictionary(item => item.Key, item => PresenceOf(item.Value), StringComparer.Ordinal);
    }

    internal static TerminalApp AppOf(ProcessEntry process, IReadOnlyDictionary<int, ProcessEntry> byPid)
        => AppOf(process, byPid, out _);

    /// <summary>
    /// Sobe pelos pais até achar o app: no iTerm2 o shell é filho do login, que é filho do
    /// iTermServer (em ~/Library/Application Support/iTerm2), que é filho do iTerm2; no
    /// Terminal.app o login é filho direto dele. Um multiplexador no caminho encerra a busca:
    /// o servidor dele é filho do launchd, então o shell de dentro não diz em que janela está
    /// (se estiver em alguma). <paramref name="multiplexer"/> diz qual foi.
    /// </summary>
    internal static TerminalApp AppOf(ProcessEntry process, IReadOnlyDictionary<int, ProcessEntry> byPid, out string? multiplexer)
    {
        multiplexer = null;
        var current = process;
        for (var step = 0; step < 64 && current is not null; step++)
        {
            if (current.Command.Contains("/iTerm", StringComparison.Ordinal)) return TerminalApp.ITerm2;
            if (current.Command.Contains("/Terminal.app/", StringComparison.Ordinal)) return TerminalApp.Terminal;
            if (MultiplexerOf(current.Command) is { } name)
            {
                multiplexer = name;
                return TerminalApp.Multiplexer;
            }
            if (current.ParentPid <= 1) break;
            current = byPid.GetValueOrDefault(current.ParentPid);
        }

        return TerminalApp.Other;
    }

    // O executável é o zmx, o tmux, o screen ou o zellij. O comm do ps traz só o caminho, que
    // pode ter espaço; a linha completa traz os argumentos, e o servidor do tmux se chama
    // "tmux: server". Com diferença de maiúsculas: "Screen Time.app" não é o screen.
    private static readonly Regex MultiplexerCommand = new(
        @"^(?:[^ ]*/)?(?<name>zmx|tmux|screen|zellij)(?::| |$)|^/.*/(?<name>zmx|tmux|screen|zellij)$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// O multiplexador do comando, se for um: <c>tmux</c>, ou <c>supacode (zmx)</c> para o zmx
    /// que vem dentro do supacode.
    /// </summary>
    internal static string? MultiplexerOf(string command)
    {
        var match = MultiplexerCommand.Match(command);
        if (!match.Success) return null;

        var name = match.Groups["name"].Value;
        return command.Contains("/supacode.app/", StringComparison.OrdinalIgnoreCase) ? $"supacode ({name})" : name;
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

    /// <summary>Saída do <c>ps -o tty=</c>: um <c>tty</c> por processo, <c>??</c> para quem não tem.</summary>
    internal static HashSet<string> ParseTtys(string output)
    {
        var ttys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.Trim() is { Length: > 0 } tty and not "??" and not "-") ttys.Add($"/dev/{tty}");

        return ttys;
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
