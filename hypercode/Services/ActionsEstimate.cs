using System.Text.Json;

namespace Hypercode.Services;

/// <summary>A duração mediana de um job, pelo nome, nos últimos runs concluídos do workflow dele.</summary>
public sealed class JobDuration
{
    public string Workflow { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> Labels { get; init; } = new();
    public double MedianSeconds { get; init; }
    public int Samples { get; init; }
}

/// <summary>As durações de um repositório e quando foram coletadas.</summary>
public sealed class RepositoryDurations
{
    public DateTimeOffset CollectedAt { get; init; }
    public List<JobDuration> Jobs { get; init; } = new();
}

/// <summary>
/// As medianas de todos os repositórios acompanhados, prontas para consulta por job. Mediana
/// com menos de <see cref="ActionsQueue.MinSamples"/> amostras não vale: é chute.
/// </summary>
public sealed class ActionsDurations
{
    public static readonly ActionsDurations Empty = new(new Dictionary<string, RepositoryDurations>());

    private readonly Dictionary<string, TimeSpan> _medians = new(StringComparer.Ordinal);

    public ActionsDurations(IReadOnlyDictionary<string, RepositoryDurations> repositories)
    {
        foreach (var (repository, durations) in repositories)
            foreach (var job in durations.Jobs.Where(job => job.Samples >= ActionsQueue.MinSamples))
                _medians[ActionsQueue.DurationKey(repository, job.Workflow, job.Name, job.Labels)] = TimeSpan.FromSeconds(job.MedianSeconds);

        CollectedAt = repositories.Count == 0 ? null : repositories.Values.Min(durations => durations.CollectedAt);
    }

    /// <summary>A coleta mais antiga entre os repositórios — a que a tela mostra.</summary>
    public DateTimeOffset? CollectedAt { get; }

    public TimeSpan? Median(ActionsJob job)
        => _medians.TryGetValue(ActionsQueue.DurationKey(job.Repository, job.Workflow, job.Name, job.Labels), out var median) ? median : null;
}

public static partial class ActionsQueue
{
    /// <summary>Runs concluídos de cada workflow que entram na mediana. A dispersão real ainda está por medir (#132).</summary>
    public const int HistoryRuns = 20;

    /// <summary>Menos amostras que isto, e o job fica sem estimativa.</summary>
    public const int MinSamples = 3;

    /// <summary>
    /// A chave de uma duração: repositório, workflow, nome do job e labels. O nome sozinho não
    /// basta — "build" existe em todo repositório — e o mesmo job em outro runner dura outra coisa.
    /// </summary>
    public static string DurationKey(string repository, string workflow, string name, IEnumerable<string> labels)
        => string.Join('\u001f', repository.ToLowerInvariant(), workflow, name, LabelKey(labels));

    public static TimeSpan Median(IReadOnlyList<TimeSpan> samples)
    {
        if (samples.Count == 0) throw new ArgumentException("sem amostras", nameof(samples));

        var ordered = samples.Order().ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }

    /// <summary>
    /// As medianas a partir das durações de cada job concluído. Pela mediana, não pela média:
    /// o job que falhou em 20 s ou travou até o timeout distorce a média, e a mediana ignora.
    /// </summary>
    public static List<JobDuration> Medians(IEnumerable<(string Workflow, string Name, IReadOnlyList<string> Labels, TimeSpan Duration)> samples)
        => samples
            .GroupBy(sample => DurationKey(string.Empty, sample.Workflow, sample.Name, sample.Labels), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new JobDuration
                {
                    Workflow = first.Workflow,
                    Name = first.Name,
                    Labels = first.Labels.ToList(),
                    MedianSeconds = Median(group.Select(sample => sample.Duration).ToList()).TotalSeconds,
                    Samples = group.Count(),
                };
            })
            .OrderBy(job => job.Workflow, StringComparer.Ordinal)
            .ThenBy(job => job.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Os ids dos workflows ativos de um repositório.</summary>
    internal static IReadOnlyList<long> ParseWorkflows(JsonElement root)
    {
        if (!root.TryGetProperty("workflows", out var workflows) || workflows.ValueKind != JsonValueKind.Array)
            return Array.Empty<long>();

        return workflows.EnumerateArray()
            .Where(workflow => workflow.ValueKind == JsonValueKind.Object
                               && string.Equals(String(workflow, "state"), "active", StringComparison.OrdinalIgnoreCase))
            .Select(workflow => Int64(workflow, "id"))
            .OfType<long>()
            .ToList();
    }

    /// <summary>
    /// A duração de cada job concluído de um run: de <c>started_at</c> a <c>completed_at</c>.
    /// Pulado e cancelado ficam de fora — não dizem quanto o job leva — e falha entra: a
    /// mediana cuida dela.
    /// </summary>
    internal static IReadOnlyList<(string Workflow, string Name, IReadOnlyList<string> Labels, TimeSpan Duration)> ParseJobDurations(
        JsonElement root,
        string workflow)
    {
        if (!root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            return Array.Empty<(string, string, IReadOnlyList<string>, TimeSpan)>();

        var result = new List<(string, string, IReadOnlyList<string>, TimeSpan)>();
        foreach (var job in jobs.EnumerateArray())
        {
            if (job.ValueKind != JsonValueKind.Object
                || !string.Equals(String(job, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                || String(job, "conclusion") is "skipped" or "cancelled"
                || Date(job, "started_at") is not { } started
                || Date(job, "completed_at") is not { } completed
                || completed <= started)
                continue;

            result.Add((String(job, "workflow_name") ?? workflow, String(job, "name") ?? "job", Labels(job), completed - started));
        }

        return result;
    }

    /// <summary>
    /// Quando cada job da fila deve começar, a partir de agora. Null é sem estimativa.
    ///
    /// Os runners online valem pelo que falta para liberarem: livre é já; ocupado é a mediana do
    /// job dele menos o tempo decorrido — nunca negativo, o job que estourou a mediana libera "a
    /// qualquer momento". Ocupado sem mediana conhecida (ou com job de repositório não
    /// acompanhado) fica fora da conta: a estimativa só usa o que sabe, e por isso tende a ser
    /// pessimista, nunca otimista.
    ///
    /// A fila anda por <c>created_at</c>, como na ordem estimada: cada job, entre os runners que
    /// atendem às labels dele, pega o que libera primeiro e o ocupa pela própria mediana. Job
    /// sem mediana ainda ganha a hora em que começa — ela só depende de quem está à frente — mas
    /// o runner que ele pega sai da conta para quem vem atrás.
    /// </summary>
    public static IReadOnlyDictionary<long, TimeSpan?> EstimateStarts(
        IEnumerable<ActionsRunner> runners,
        IEnumerable<ActionsJob> jobs,
        Func<ActionsJob, TimeSpan?> median,
        DateTimeOffset now)
    {
        var all = jobs.ToList();
        var online = runners.Where(runner => runner.IsOnline).ToList();
        var running = JobsByRunner(online, all);

        // Quanto falta para cada runner liberar; ausente é desconhecido.
        var freeIn = new Dictionary<long, TimeSpan>();
        foreach (var runner in online)
        {
            if (!runner.IsBusy)
                freeIn[runner.Id] = TimeSpan.Zero;
            else if (running.TryGetValue(runner.Id, out var job) && median(job) is { } duration)
                freeIn[runner.Id] = Max(TimeSpan.Zero, duration - (now - (job.StartedAt ?? job.CreatedAt)));
        }

        var result = new Dictionary<long, TimeSpan?>();
        foreach (var job in all.Where(job => job.IsQueued).OrderBy(job => job.CreatedAt).ThenBy(job => job.Id))
        {
            var chosen = online
                .Where(runner => freeIn.ContainsKey(runner.Id) && Serves(runner, job))
                .OrderBy(runner => freeIn[runner.Id])
                .ThenBy(runner => runner.Id)
                .FirstOrDefault();

            if (chosen is null)
            {
                result[job.Id] = null;
                continue;
            }

            var start = freeIn[chosen.Id];
            result[job.Id] = start;

            if (median(job) is { } duration) freeIn[chosen.Id] = start + duration;
            else freeIn.Remove(chosen.Id);
        }

        return result;
    }

    /// <summary>O runner tem todas as labels que o job pede, sem caixa.</summary>
    public static bool Serves(ActionsRunner runner, ActionsJob job)
    {
        var labels = runner.Labels.Select(label => label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return job.Labels.Select(label => label.Trim()).Where(label => label.Length > 0).All(labels.Contains);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
