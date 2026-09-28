namespace Hypercode.Services;

/// <summary>Um worktree que a limpeza automática removeu.</summary>
public sealed record AutoCleanupEntry(DateTimeOffset At, string Name, string Reason);

/// <summary>
/// Memória da limpeza automática: desde quando cada worktree está concluído (a carência), até
/// quando ele fica adiado depois de ser pulado, por que ficou (os pendentes) e o que já saiu
/// (o histórico). Tudo por caminho e só em memória — reabrir o app recomeça a carência, que é
/// o lado seguro. Não faz I/O: quem confere e remove é o <c>MainViewModel</c>.
/// </summary>
public sealed class AutoCleanupTracker
{
    public const int DefaultGraceMinutes = 10;

    /// <summary>Pulado por estar em uso: o terminal pode fechar a qualquer momento.</summary>
    public static readonly TimeSpan InUseRetry = TimeSpan.FromMinutes(1);

    /// <summary>Pulado por arquivo ignorado ou recusa do git: só muda quando alguém mexe na pasta.</summary>
    public static readonly TimeSpan BlockedRetry = TimeSpan.FromMinutes(5);

    private const int HistoryLimit = 20;

    private readonly Dictionary<string, DateTimeOffset> _completedSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _retryAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Name, string Reason)> _pending = new(StringComparer.Ordinal);
    private readonly List<AutoCleanupEntry> _history = new();

    /// <summary>Removidos, do mais recente ao mais antigo.</summary>
    public IReadOnlyList<AutoCleanupEntry> History => _history;

    /// <summary>Concluídos que a limpeza automática deixou, com o motivo — ficam para a mão.</summary>
    public IReadOnlyList<(string Path, string Name, string Reason)> Pending
        => _pending.OrderBy(item => item.Value.Name).Select(item => (item.Key, item.Value.Name, item.Value.Reason)).ToList();

    /// <summary>
    /// Os concluídos de agora. Quem aparece pela primeira vez começa a carência; quem deixou de
    /// estar concluído (ou sumiu) é esquecido, inclusive o adiamento e o motivo pendente.
    /// </summary>
    public void Observe(IReadOnlyCollection<string> completedPaths, DateTimeOffset now)
    {
        var keep = completedPaths.ToHashSet(StringComparer.Ordinal);
        foreach (var path in completedPaths) _completedSince.TryAdd(path, now);

        foreach (var stale in _completedSince.Keys.Where(path => !keep.Contains(path)).ToList())
        {
            _completedSince.Remove(stale);
            _retryAt.Remove(stale);
            _pending.Remove(stale);
        }
    }

    /// <summary>Passou da carência e não está adiado.</summary>
    public bool IsDue(string path, TimeSpan grace, DateTimeOffset now)
        => _completedSince.TryGetValue(path, out var since)
           && now - since >= grace
           && (!_retryAt.TryGetValue(path, out var retry) || now >= retry);

    /// <summary>Quanto falta da carência; zero se já passou ou se o caminho não é conhecido.</summary>
    public TimeSpan GraceRemaining(string path, TimeSpan grace, DateTimeOffset now)
        => _completedSince.TryGetValue(path, out var since) && since + grace > now ? since + grace - now : TimeSpan.Zero;

    /// <summary>
    /// Deixa o worktree para depois, com o motivo. Devolve se o motivo é novo — é quando vale
    /// dizer no rodapé; repetir o mesmo a cada ciclo seria ruído.
    /// </summary>
    public bool Skip(string path, string name, string reason, TimeSpan? retryAfter, DateTimeOffset now)
    {
        if (retryAfter is { } delay) _retryAt[path] = now + delay;

        var isNew = !_pending.TryGetValue(path, out var previous) || previous.Reason != reason;
        _pending[path] = (name, reason);
        return isNew;
    }

    public void RecordRemoval(string path, string name, string reason, DateTimeOffset now)
    {
        _completedSince.Remove(path);
        _retryAt.Remove(path);
        _pending.Remove(path);

        _history.Insert(0, new AutoCleanupEntry(now, name, reason));
        if (_history.Count > HistoryLimit) _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);
    }

    /// <summary>Repositório trocado ou opção desligada: carências e pendências recomeçam. O histórico fica.</summary>
    public void Reset()
    {
        _completedSince.Clear();
        _retryAt.Clear();
        _pending.Clear();
    }

    /// <summary>
    /// Arquivo ignorado que pode ir embora sem perguntar: saída de build do .NET, que o próximo
    /// build refaz. O resto — .env, appsettings.Development.json, banco local — segura o worktree.
    /// </summary>
    public static bool IsDisposableIgnored(string entry)
    {
        if (!entry.EndsWith('/')) return false;
        var name = Path.GetFileName(entry.TrimEnd('/'));
        return name is "bin" or "obj";
    }

    /// <summary>
    /// A pasta de trabalho de cada processo do usuário, pelo lsof (`-d cwd`, que é rápido: não
    /// varre a árvore como o `+D`). Pega o iTerm2 aberto pelo duplo-clique, o claude, o
    /// dotnet watch. Null se o lsof falhou — aí nada deve ser removido.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ListProcessDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        var lsof = ExecutableLocator.Find("lsof") ?? (File.Exists("/usr/sbin/lsof") ? "/usr/sbin/lsof" : null);
        if (lsof is null) return null;

        try
        {
            var result = await ProcessRunner.RunAsync(
                lsof,
                new[] { "-n", "-P", "-w", "-d", "cwd", "-Fn" },
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // O lsof sai com 1 quando algum processo não pôde ser lido, mesmo listando os demais.
            if (result.StandardOutput.Length == 0) return null;

            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith('n'))
                .Select(line => line[1..])
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Algum desses diretórios é o worktree ou fica dentro dele.</summary>
    public static bool IsInUse(string worktreePath, IReadOnlyList<string> processDirectories)
    {
        var root = worktreePath.TrimEnd('/');
        return processDirectories.Any(directory =>
            string.Equals(directory.TrimEnd('/'), root, StringComparison.OrdinalIgnoreCase)
            || directory.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }
}
