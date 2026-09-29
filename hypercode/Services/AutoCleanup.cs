using System.Text.Json;

namespace Hypercode.Services;

/// <summary>Um worktree que a limpeza automática removeu.</summary>
public sealed record AutoCleanupEntry(DateTimeOffset At, string Name, string Reason);

/// <summary>O que sobrevive ao fechamento do app, de um repositório: os carimbos da carência e o histórico.</summary>
public sealed class AutoCleanupState
{
    public Dictionary<string, DateTimeOffset> CompletedSince { get; set; } = new(StringComparer.Ordinal);
    public List<AutoCleanupEntry> History { get; set; } = new();

    public bool IsEmpty => CompletedSince.Count == 0 && History.Count == 0;
}

/// <summary>
/// Memória da limpeza automática: desde quando cada worktree está concluído (a carência), até
/// quando ele fica adiado depois de ser pulado, por que ficou (os pendentes) e o que já saiu
/// (o histórico). Tudo por caminho. A carência e o histórico vão para disco pelo
/// <see cref="AutoCleanupStore"/> — senão quem abre e fecha o app nunca chega ao fim da
/// carência —; adiamento e pendência são do momento e recomeçam a cada abertura. Não faz I/O:
/// quem confere, remove e grava é o <c>MainViewModel</c>.
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

    /// <summary>Mudou a carência ou o histórico desde a última <see cref="Snapshot"/> gravada.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Removidos, do mais recente ao mais antigo.</summary>
    public IReadOnlyList<AutoCleanupEntry> History => _history;

    /// <summary>Concluídos que a limpeza automática deixou, com o motivo — ficam para a mão.</summary>
    public IReadOnlyList<(string Path, string Name, string Reason)> Pending
        => _pending.OrderBy(item => item.Value.Name).Select(item => (item.Key, item.Value.Name, item.Value.Reason)).ToList();

    /// <summary>
    /// Os concluídos de agora. Quem aparece pela primeira vez começa a carência. Quem deixou de
    /// ser candidato — <paramref name="isRefuted"/>: a pasta saiu da lista, o PR foi reaberto —
    /// é esquecido, inclusive o adiamento e o motivo pendente. Não estar concluído neste tique
    /// não basta: o PR e o status chegam depois da lista e a consulta pode falhar, e esquecer
    /// nessa janela apagava o carimbo gravado, recomeçando a carência a cada abertura.
    /// </summary>
    public void Observe(IReadOnlyCollection<string> completedPaths, Func<string, bool> isRefuted, DateTimeOffset now)
    {
        foreach (var path in completedPaths)
            if (_completedSince.TryAdd(path, now)) IsDirty = true;

        var keep = completedPaths.ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _completedSince.Keys.Where(path => !keep.Contains(path) && isRefuted(path)).ToList())
        {
            _completedSince.Remove(stale);
            _retryAt.Remove(stale);
            _pending.Remove(stale);
            IsDirty = true;
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
    /// O que a limpeza automática sabe deste worktree, para o tooltip: desde quando ela o vê
    /// concluído — o carimbo de onde a carência conta — e quando ele pode sair ou por que ficou.
    /// Null se ela ainda não o viu. Horários locais, como no histórico: um carimbo que muda
    /// entre dois tiques é a carência recomeçando, e é isso que precisa ficar visível.
    /// </summary>
    public string? Describe(string path, TimeSpan grace, DateTimeOffset now, DateTimeOffset startsAt)
    {
        if (!_completedSince.TryGetValue(path, out var since)) return null;

        static string At(DateTimeOffset moment) => $"{moment.ToLocalTime():dd/MM HH:mm}";

        var lines = new List<string> { $"Visto concluído pela limpeza automática em {At(since)}." };
        DateTimeOffset? retry = _retryAt.TryGetValue(path, out var retryAt) && retryAt > now ? retryAt : null;

        if (_pending.TryGetValue(path, out var pending))
        {
            lines.Add($"Mantido: {pending.Reason}.");
            if (retry is { } next) lines.Add($"Nova tentativa a partir de {At(next)}.");
            return string.Join("\n", lines);
        }

        var dueAt = since + grace;
        if (startsAt > dueAt) dueAt = startsAt;
        if (retry is { } later && later > dueAt) dueAt = later;

        lines.Add(dueAt > now
            ? $"Carência de {grace.TotalMinutes:0} min: sai a partir de {At(dueAt)}."
            : "Carência vencida: sai no próximo tique do monitoramento, se a janela não estiver minimizada.");
        return string.Join("\n", lines);
    }

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
        IsDirty = true;
    }

    /// <summary>Opção ligada ou desligada: carências e pendências recomeçam. O histórico fica.</summary>
    public void Reset()
    {
        if (_completedSince.Count > 0) IsDirty = true;
        _completedSince.Clear();
        _retryAt.Clear();
        _pending.Clear();
    }

    /// <summary>
    /// Repositório trocado ou app aberto: troca tudo pelo que estava gravado desse repositório.
    /// Carimbo no futuro — relógio que andou para trás, backup restaurado — não pode tornar o
    /// worktree elegível na hora: vira <paramref name="now"/> e a carência recomeça.
    /// </summary>
    public void Restore(AutoCleanupState state, DateTimeOffset now)
    {
        _completedSince.Clear();
        _retryAt.Clear();
        _pending.Clear();
        _history.Clear();
        IsDirty = false;

        foreach (var (path, since) in state.CompletedSince)
        {
            if (since > now) IsDirty = true;
            _completedSince[path] = since > now ? now : since;
        }

        _history.AddRange(state.History.OrderByDescending(entry => entry.At).Take(HistoryLimit));
    }

    /// <summary>O que gravar, em UTC. Zera o <see cref="IsDirty"/>: quem pede é quem grava.</summary>
    public AutoCleanupState Snapshot()
    {
        IsDirty = false;
        return new AutoCleanupState
        {
            CompletedSince = _completedSince.ToDictionary(item => item.Key, item => item.Value.ToUniversalTime(), StringComparer.Ordinal),
            History = _history.Select(entry => entry with { At = entry.At.ToUniversalTime() }).ToList(),
        };
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

/// <summary>
/// O <see cref="AutoCleanupState"/> de cada repositório, em autocleanup.json ao lado do
/// settings.json. Arquivo ausente, ilegível ou corrompido vale como vazio — é o app recém-
/// instalado, a carência recomeça. Cada gravação poda o que já foi embora dos outros
/// repositórios, senão o arquivo cresce para sempre.
/// </summary>
public sealed class AutoCleanupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AutoCleanupStore(string filePath) => _filePath = filePath;

    public static AutoCleanupStore Default { get; } = new(Path.Combine(SettingsStore.DataDirectory, "autocleanup.json"));

    public AutoCleanupState Load(string repository)
        => ReadAll().TryGetValue(repository, out var state) ? state : new AutoCleanupState();

    public void Save(string repository, AutoCleanupState state)
    {
        try
        {
            var all = ReadAll();

            // Os outros repositórios não são observados agora: fica só o que ainda existe no disco.
            // O do momento o tracker já podou — inclusive o órfão, que não tem pasta e segue concluído.
            foreach (var (other, otherState) in all.Where(item => item.Key != repository).ToList())
            {
                if (!Directory.Exists(other))
                {
                    all.Remove(other);
                    continue;
                }

                foreach (var gone in otherState.CompletedSince.Keys.Where(path => !Directory.Exists(path)).ToList())
                    otherState.CompletedSince.Remove(gone);
                if (otherState.IsEmpty) all.Remove(other);
            }

            if (state.IsEmpty) all.Remove(repository);
            else all[repository] = state;

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch
        {
            // Como as preferências: falhar ao gravar não derruba o app, só recomeça a carência.
        }
    }

    private Dictionary<string, AutoCleanupState> ReadAll()
    {
        try
        {
            if (File.Exists(_filePath)
                && JsonSerializer.Deserialize<Dictionary<string, AutoCleanupState>>(File.ReadAllText(_filePath), JsonOptions) is { } all)
            {
                return all
                    .Where(item => item.Value is not null)
                    .ToDictionary(
                        item => item.Key,
                        item => new AutoCleanupState
                        {
                            CompletedSince = new Dictionary<string, DateTimeOffset>(item.Value.CompletedSince ?? new(), StringComparer.Ordinal),
                            History = (item.Value.History ?? new()).Where(entry => entry is not null).ToList(),
                        },
                        StringComparer.Ordinal);
            }
        }
        catch
        {
            // Corrompido: vale como vazio, e a próxima gravação o substitui.
        }

        return new Dictionary<string, AutoCleanupState>(StringComparer.Ordinal);
    }
}
