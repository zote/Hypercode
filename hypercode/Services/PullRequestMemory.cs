using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypercode.Services;

/// <summary>O que se guarda de um PR para perceber, na próxima leitura, que ele mudou.</summary>
public sealed record PullRequestSnapshot
{
    public string State { get; init; } = string.Empty;
    public ChecksState Checks { get; init; }
    public ReviewState Review { get; init; }
    public bool HasConflicts { get; init; }
    public bool IsBehindBase { get; init; }

    /// <summary>Transições avisadas e ainda não marcadas como vistas — o destaque da linha.</summary>
    public List<string> Unseen { get; init; } = new();

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// O GitHub calcula a mergeabilidade sob demanda e devolve UNKNOWN enquanto isso. Nesse
    /// caso vale o que se sabia antes — senão um conflito "some" e "volta", e avisa duas vezes.
    /// </summary>
    public static PullRequestSnapshot Of(PullRequestInfo pullRequest, PullRequestSnapshot? previous)
    {
        var mergeabilityKnown =
            pullRequest.Mergeable is { } mergeable && !mergeable.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);

        return new PullRequestSnapshot
        {
            State = pullRequest.State.ToUpperInvariant(),
            Checks = pullRequest.Checks,
            Review = pullRequest.Review,
            HasConflicts = mergeabilityKnown || previous is null ? pullRequest.HasConflicts : previous.HasConflicts,
            IsBehindBase = mergeabilityKnown || previous is null ? pullRequest.IsBehindBase : previous.IsBehindBase,
            Unseen = previous?.Unseen.ToList() ?? new List<string>(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// As mudanças de <paramref name="before"/> para <paramref name="after"/> que pedem ação,
    /// em texto curto. Só transições: o mesmo estado lido de novo não gera nada.
    /// </summary>
    internal static IReadOnlyList<string> Transitions(PullRequestSnapshot before, PullRequestSnapshot after, string? failingChecks = null)
    {
        var changes = new List<string>();

        if (before.State != after.State)
        {
            changes.Add(after.State switch
            {
                "MERGED" => "mergeado — o worktree pode ser limpo",
                "CLOSED" => "fechado sem merge — o worktree pode ser limpo",
                "OPEN" => "reaberto",
                _ => after.State.ToLowerInvariant(),
            });
        }

        // Checks, review e mergeabilidade só interessam com o PR aberto.
        if (after.State != "OPEN") return changes;

        if (before.Checks != after.Checks)
        {
            if (after.Checks == ChecksState.Failing)
                changes.Add(string.IsNullOrEmpty(failingChecks) ? "checks falhando" : $"checks falhando: {failingChecks}");
            else if (after.Checks == ChecksState.Passing)
                changes.Add("checks passaram");
        }

        if (before.Review != after.Review)
        {
            if (after.Review == ReviewState.Approved) changes.Add("review aprovado");
            else if (after.Review == ReviewState.ChangesRequested) changes.Add("review pediu mudanças");
        }

        if (!before.HasConflicts && after.HasConflicts) changes.Add("conflito com a base");
        else if (!before.IsBehindBase && after.IsBehindBase && !after.HasConflicts) changes.Add("a base andou");

        return changes;
    }
}

/// <summary>
/// Último estado visto de cada PR, gravado em disco: é o que permite avisar só na transição
/// — e não de novo a cada reabertura do app. A chave é a URL do PR, que já inclui o repositório.
/// </summary>
public sealed class PullRequestMemory
{
    /// <summary>Acima disto, os PRs lidos há mais tempo são esquecidos.</summary>
    private const int MaxEntries = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly Dictionary<string, PullRequestSnapshot> _entries;
    private bool _dirty;

    private PullRequestMemory(string filePath, Dictionary<string, PullRequestSnapshot> entries)
    {
        _filePath = filePath;
        _entries = entries;
    }

    public static PullRequestMemory Load() => Load(Path.Combine(SettingsStore.DataDirectory, "pull-requests.json"));

    public static PullRequestMemory Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath)
                && JsonSerializer.Deserialize<Dictionary<string, PullRequestSnapshot>>(File.ReadAllText(filePath), JsonOptions) is { } entries)
                return new PullRequestMemory(filePath, new Dictionary<string, PullRequestSnapshot>(entries, StringComparer.Ordinal));
        }
        catch
        {
            // Arquivo corrompido: começa do zero — no pior caso, deixa de avisar uma transição.
        }

        return new PullRequestMemory(filePath, new Dictionary<string, PullRequestSnapshot>(StringComparer.Ordinal));
    }

    private static string KeyOf(PullRequestInfo pullRequest)
        => string.IsNullOrEmpty(pullRequest.Url) ? $"{pullRequest.HeadRefName}#{pullRequest.Number}" : pullRequest.Url;

    /// <summary>
    /// Registra o estado lido agora e devolve as transições desde a leitura anterior. PR nunca
    /// visto não gera aviso: é a primeira foto dele, não uma mudança.
    /// </summary>
    public IReadOnlyList<string> Observe(PullRequestInfo pullRequest)
    {
        var key = KeyOf(pullRequest);
        _entries.TryGetValue(key, out var previous);

        var current = PullRequestSnapshot.Of(pullRequest, previous);
        var transitions = previous is null
            ? Array.Empty<string>()
            : PullRequestSnapshot.Transitions(previous, current, pullRequest.FailingChecksSummary);

        current.Unseen.AddRange(transitions);

        if (previous is null || !SameState(previous, current)) _dirty = true;
        _entries[key] = current;

        return transitions;
    }

    /// <summary>O que foi avisado neste PR e ainda não foi marcado como visto.</summary>
    public IReadOnlyList<string> Unseen(PullRequestInfo pullRequest)
        => _entries.TryGetValue(KeyOf(pullRequest), out var entry) ? entry.Unseen : Array.Empty<string>();

    public void MarkSeen(PullRequestInfo pullRequest)
    {
        if (!_entries.TryGetValue(KeyOf(pullRequest), out var entry) || entry.Unseen.Count == 0) return;

        _entries[KeyOf(pullRequest)] = entry with { Unseen = new List<string>() };
        _dirty = true;
    }

    public void Save()
    {
        if (!_dirty) return;

        try
        {
            if (_entries.Count > MaxEntries)
            {
                foreach (var stale in _entries.OrderBy(entry => entry.Value.UpdatedAt).Take(_entries.Count - MaxEntries).ToList())
                    _entries.Remove(stale.Key);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, JsonOptions));
            _dirty = false;
        }
        catch
        {
            // Como as preferências: falhar ao gravar não derruba o app, só pode repetir um aviso.
        }
    }

    /// <summary>Só o horário mudou? Então não vale regravar o arquivo a cada ciclo.</summary>
    private static bool SameState(PullRequestSnapshot left, PullRequestSnapshot right)
        => left.State == right.State
           && left.Checks == right.Checks
           && left.Review == right.Review
           && left.HasConflicts == right.HasConflicts
           && left.IsBehindBase == right.IsBehindBase
           && left.Unseen.SequenceEqual(right.Unseen);
}
