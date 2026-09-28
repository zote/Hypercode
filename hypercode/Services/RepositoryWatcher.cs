using System.Collections.Concurrent;

namespace Hypercode.Services;

/// <summary>O que mudou no git dir, do mais barato ao mais caro de reler.</summary>
[Flags]
public enum RepositoryChange
{
    None = 0,

    /// <summary>Refs ou operação pendente: basta reler o `git status` das linhas.</summary>
    Status = 1,

    /// <summary>Worktree criado, removido, travado ou com a branch trocada: relê a lista.</summary>
    Worktrees = 2,
}

/// <summary>
/// Observa o git dir comum do repositório (FSEvents no macOS, sem polling) e avisa, com
/// debounce, quando algo que a lista mostra pode ter mudado. O que não interessa — objects,
/// logs, index, FETCH_HEAD — é descartado no próprio evento, sem custo além do filtro.
/// O index fica de fora de propósito: o próprio `git status` que o app roda pode regravá-lo.
/// </summary>
public sealed class RepositoryWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(800);

    private static readonly HashSet<string> WorktreeFiles = new(StringComparer.Ordinal) { "HEAD", "locked", "gitdir" };

    private static readonly HashSet<string> OperationMarkers = new(StringComparer.Ordinal)
    {
        "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG", "rebase-merge", "rebase-apply",
    };

    private static readonly ConcurrentDictionary<string, Task<RepositoryWatcher>> Stalled = new(StringComparer.Ordinal);

    private readonly FileSystemWatcher _watcher;
    private readonly Action<RepositoryChange> _onChanged;
    private readonly SynchronizationContext? _context;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private RepositoryChange _pending;
    private volatile bool _disposed;

    private RepositoryWatcher(string gitCommonDir, Action<RepositoryChange> onChanged, SynchronizationContext? context)
    {
        GitCommonDir = Path.TrimEndingDirectorySeparator(gitCommonDir);
        _onChanged = onChanged;
        _context = context;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(GitCommonDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };

        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Changed += OnEvent;
        _watcher.Renamed += OnRenamed;

        // Buffer estourado ou watcher perdido: não dá para saber o que mudou, relê tudo.
        _watcher.Error += (_, _) => Schedule(RepositoryChange.Worktrees);

        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher.Dispose();
            _timer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Cria o watcher fora da thread de quem chama. Ligar o FileSystemWatcher registra o
    /// FSEvents de forma síncrona, e num volume lento ou numa máquina sob carga isso leva
    /// minutos — na UI, congelaria o app. Se não fica pronto em <paramref name="timeout"/>,
    /// devolve null e descarta o watcher quando (e se) ele terminar de subir.
    /// </summary>
    /// <param name="onChanged">Chamado em <paramref name="context"/> — capturado por quem chama,
    /// porque na thread do pool não há contexto nenhum para capturar.</param>
    public static async Task<RepositoryWatcher?> CreateAsync(
        string gitCommonDir,
        Action<RepositoryChange> onChanged,
        SynchronizationContext? context,
        TimeSpan timeout)
    {
        var key = Path.TrimEndingDirectorySeparator(gitCommonDir);

        // Uma subida que estourou o prazo segue presa numa thread do pool; a próxima tentativa
        // espera por ela em vez de prender outra.
        var creation = Stalled.TryRemove(key, out var stalled)
            ? stalled
            : Task.Run(() => new RepositoryWatcher(gitCommonDir, onChanged, context));

        if (await Task.WhenAny(creation, Task.Delay(timeout)).ConfigureAwait(false) == creation)
            return await creation.ConfigureAwait(false);

        Stalled[key] = creation;
        _ = creation.ContinueWith(
            task =>
            {
                // Ninguém veio buscar: descarta.
                if (Stalled.TryRemove(KeyValuePair.Create(key, task))) task.Result.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        return null;
    }

    public string GitCommonDir { get; }

    private void OnEvent(object sender, FileSystemEventArgs e) => Schedule(Classify(RelativePath(e.FullPath)));

    private void OnRenamed(object sender, RenamedEventArgs e)
        => Schedule(Classify(RelativePath(e.FullPath)) | Classify(RelativePath(e.OldFullPath)));

    private string? RelativePath(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;
        var relative = Path.GetRelativePath(GitCommonDir, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative;
    }

    /// <summary>Caminho relativo ao git dir comum → o que precisa ser relido.</summary>
    internal static RepositoryChange Classify(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return RepositoryChange.None;

        var parts = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[^1].EndsWith(".lock", StringComparison.Ordinal)) return RepositoryChange.None;

        switch (parts[0])
        {
            // HEAD do principal: trocou de branch (ou commitou em detached).
            case "HEAD":
                return parts.Length == 1 ? RepositoryChange.Worktrees | RepositoryChange.Status : RepositoryChange.None;

            case "packed-refs":
                return parts.Length == 1 ? RepositoryChange.Status : RepositoryChange.None;

            case "refs":
                return parts.Length > 1 && parts[1] is "heads" or "remotes" ? RepositoryChange.Status : RepositoryChange.None;

            // worktrees/<nome> é a pasta administrativa de cada worktree vinculado.
            case "worktrees":
                if (parts.Length <= 2) return RepositoryChange.Worktrees;
                if (parts.Length == 3 && WorktreeFiles.Contains(parts[2]))
                    return parts[2] == "HEAD"
                        ? RepositoryChange.Worktrees | RepositoryChange.Status
                        : RepositoryChange.Worktrees;
                return OperationMarkers.Contains(parts[2]) ? RepositoryChange.Status : RepositoryChange.None;

            default:
                return OperationMarkers.Contains(parts[0]) ? RepositoryChange.Status : RepositoryChange.None;
        }
    }

    private void Schedule(RepositoryChange change)
    {
        if (change == RepositoryChange.None) return;

        lock (_gate)
        {
            _pending |= change;
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        RepositoryChange change;
        lock (_gate)
        {
            change = _pending;
            _pending = RepositoryChange.None;
        }

        if (change == RepositoryChange.None || _disposed) return;

        if (_context is null) _onChanged(change);
        else _context.Post(_ => { if (!_disposed) _onChanged(change); }, null);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
