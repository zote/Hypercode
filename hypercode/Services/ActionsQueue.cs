using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypercode.Services;

/// <summary>Um runner self-hosted como o GitHub o lista, com o grupo dele.</summary>
public sealed record ActionsRunner(
    long Id,
    string Name,
    string Os,
    bool IsOnline,
    bool IsBusy,
    IReadOnlyList<string> Labels,
    string Group);

/// <summary>
/// Um job de workflow de um repositório acompanhado, já com o que se sabe do run dele (branch
/// e PR). As labels de um job na fila são as do <c>runs-on</c> já resolvido — inclusive quando
/// o YAML usa expressão com variável.
/// </summary>
public sealed record ActionsJob(
    long Id,
    string Repository,
    string Name,
    string Workflow,
    string Status,
    IReadOnlyList<string> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    long? RunnerId,
    string? RunnerName,
    string? Branch,
    int? PullRequest,
    string? Url)
{
    public bool IsQueued => string.Equals(Status, "queued", StringComparison.OrdinalIgnoreCase);

    public bool IsRunning => string.Equals(Status, "in_progress", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Um run na fila sem job nenhum criado. Job que espera um <c>needs</c> não aparece na API,
/// mas o run inteiro sem job é outra coisa: costuma ser o <c>concurrency</c> segurando, ou o
/// GitHub ainda criando os jobs. A API não diz qual dos dois.
/// </summary>
public sealed record ActionsWaitingRun(
    long Id,
    string Repository,
    string Workflow,
    string? Branch,
    int? PullRequest,
    DateTimeOffset CreatedAt,
    string? Url);

/// <summary>
/// Uma fila: os jobs que esperam o mesmo conjunto de labels. Um job pedindo <c>mac-studio</c>
/// não disputa com um pedindo <c>ARM64</c>, então cada conjunto é uma fila separada.
/// </summary>
public sealed record ActionsLane(IReadOnlyList<string> Labels, IReadOnlyList<ActionsJob> Jobs);

/// <summary>O que um ciclo trouxe. Cada parte pode falhar sozinha: sem admin na org, a fila segue.</summary>
public sealed record ActionsSnapshot(
    IReadOnlyList<ActionsRunner> Runners,
    IReadOnlyList<ActionsJob> Jobs,
    IReadOnlyList<ActionsWaitingRun> WaitingRuns,
    IReadOnlyList<string> RunnerProblems,
    IReadOnlyList<string> QueueProblems,
    ApiBudget? Budget,
    DateTimeOffset ReadAt);

/// <summary>A conta da fila: tudo puro, sem I/O, para os testes não precisarem do GitHub.</summary>
public static partial class ActionsQueue
{
    /// <summary>
    /// As filas: os jobs <c>queued</c>, agrupados pelo conjunto de labels (sem caixa nem ordem)
    /// e ordenados por <c>created_at</c>. É estimativa: o despacho para self-hosted é FIFO na
    /// prática, mas o GitHub não expõe posição nem promete a ordem. Fila com mais jobs primeiro;
    /// empate, a que espera há mais tempo.
    /// </summary>
    public static IReadOnlyList<ActionsLane> BuildLanes(IEnumerable<ActionsJob> jobs)
        => jobs
            .Where(job => job.IsQueued)
            .GroupBy(job => LabelKey(job.Labels), StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(job => job.CreatedAt).ThenBy(job => job.Id).ToList();
                var labels = ordered[0].Labels
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new ActionsLane(labels, ordered);
            })
            .OrderByDescending(lane => lane.Jobs.Count)
            .ThenBy(lane => lane.Jobs[0].CreatedAt)
            .ToList();

    /// <summary>A chave de um conjunto de labels: sem repetição, sem caixa, em ordem.</summary>
    public static string LabelKey(IEnumerable<string> labels)
        => string.Join('\n', labels
            .Select(label => label.Trim().ToLowerInvariant())
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    /// <summary>
    /// O job que cada runner ocupado está rodando, entre os dos repositórios acompanhados. Casa
    /// pelo id e, sem ele, pelo nome. Runner ocupado com job de repositório não acompanhado
    /// simplesmente não aparece aqui.
    /// </summary>
    public static IReadOnlyDictionary<long, ActionsJob> JobsByRunner(
        IEnumerable<ActionsRunner> runners,
        IEnumerable<ActionsJob> jobs)
    {
        var running = jobs.Where(job => job.IsRunning).ToList();
        var result = new Dictionary<long, ActionsJob>();

        foreach (var runner in runners.Where(runner => runner.IsBusy))
        {
            var job = running.FirstOrDefault(job => job.RunnerId == runner.Id)
                      ?? running.FirstOrDefault(job => string.Equals(job.RunnerName, runner.Name, StringComparison.Ordinal));
            if (job is not null) result[runner.Id] = job;
        }

        return result;
    }

    /// <summary>
    /// owner/repo de uma linha da configuração: aceita owner/repo, a URL do GitHub (com ou sem
    /// .git) e o remoto SSH. Null para o que não for nada disso.
    /// </summary>
    public static string? NormalizeRepository(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = RepositoryPattern().Match(text.Trim());
        return match.Success ? $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}" : null;
    }

    /// <summary>A lista da configuração: uma por linha (ou separadas por vírgula), sem repetir.</summary>
    public static IReadOnlyList<string> ParseRepositories(string? text)
        => (text ?? string.Empty)
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRepository)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex(@"^(?:(?:https?://)?github\.com/|git@github\.com:)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)/(?<repo>[A-Za-z0-9._-]+?)(?:\.git)?/?$")]
    private static partial Regex RepositoryPattern();

    // ── Leitura do JSON da API REST ─────────────────────────────────────────

    /// <summary>A lista de runners (da org, de um grupo ou do repositório), todos com o grupo dado.</summary>
    internal static IReadOnlyList<ActionsRunner> ParseRunners(JsonElement root, string group)
    {
        if (!root.TryGetProperty("runners", out var runners) || runners.ValueKind != JsonValueKind.Array)
            return Array.Empty<ActionsRunner>();

        return runners.EnumerateArray()
            .Where(runner => runner.ValueKind == JsonValueKind.Object)
            .Select(runner => new ActionsRunner(
                Int64(runner, "id") ?? 0,
                String(runner, "name") ?? "runner",
                String(runner, "os") ?? string.Empty,
                string.Equals(String(runner, "status"), "online", StringComparison.OrdinalIgnoreCase),
                runner.TryGetProperty("busy", out var busy) && busy.ValueKind == JsonValueKind.True,
                Labels(runner),
                group))
            .ToList();
    }

    /// <summary>Os grupos de runner da org: id e nome.</summary>
    internal static IReadOnlyList<(long Id, string Name)> ParseRunnerGroups(JsonElement root)
    {
        if (!root.TryGetProperty("runner_groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return Array.Empty<(long, string)>();

        return groups.EnumerateArray()
            .Where(group => group.ValueKind == JsonValueKind.Object && Int64(group, "id") is not null)
            .Select(group => (Int64(group, "id")!.Value, String(group, "name") ?? "grupo"))
            .ToList();
    }

    /// <summary>Um run como o <c>/actions/runs</c> o devolve: o que os jobs dele herdam.</summary>
    internal sealed record RunInfo(long Id, string Workflow, string Status, string? Branch, int? PullRequest, DateTimeOffset CreatedAt, string? Url);

    internal static IReadOnlyList<RunInfo> ParseRuns(JsonElement root)
    {
        if (!root.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            return Array.Empty<RunInfo>();

        return runs.EnumerateArray()
            .Where(run => run.ValueKind == JsonValueKind.Object && Int64(run, "id") is not null)
            .Select(run => new RunInfo(
                Int64(run, "id")!.Value,
                String(run, "name") ?? "workflow",
                String(run, "status") ?? string.Empty,
                String(run, "head_branch"),
                FirstPullRequest(run),
                Date(run, "created_at") ?? DateTimeOffset.MinValue,
                String(run, "html_url")))
            .ToList();
    }

    /// <summary>Os jobs de um run, com a branch e o PR do run.</summary>
    internal static IReadOnlyList<ActionsJob> ParseJobs(JsonElement root, string repository, RunInfo run)
    {
        if (!root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            return Array.Empty<ActionsJob>();

        return jobs.EnumerateArray()
            .Where(job => job.ValueKind == JsonValueKind.Object && Int64(job, "id") is not null)
            .Select(job => new ActionsJob(
                Int64(job, "id")!.Value,
                repository,
                String(job, "name") ?? "job",
                String(job, "workflow_name") ?? run.Workflow,
                String(job, "status") ?? string.Empty,
                Labels(job),
                Date(job, "created_at") ?? run.CreatedAt,
                Date(job, "started_at"),
                Int64(job, "runner_id") is > 0 and var id ? id : null,
                String(job, "runner_name") is { Length: > 0 } name ? name : null,
                String(job, "head_branch") ?? run.Branch,
                run.PullRequest,
                String(job, "html_url")))
            .ToList();
    }

    private static int? FirstPullRequest(JsonElement run)
    {
        if (!run.TryGetProperty("pull_requests", out var pullRequests) || pullRequests.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var pullRequest in pullRequests.EnumerateArray())
            if (pullRequest.ValueKind == JsonValueKind.Object
                && pullRequest.TryGetProperty("number", out var number)
                && number.TryGetInt32(out var value))
                return value;

        return null;
    }

    /// <summary>Runner traz labels como objetos (<c>{name}</c>); job, como strings.</summary>
    private static IReadOnlyList<string> Labels(JsonElement element)
    {
        if (!element.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return labels.EnumerateArray()
            .Select(label => label.ValueKind switch
            {
                JsonValueKind.String => label.GetString(),
                JsonValueKind.Object => String(label, "name"),
                _ => null,
            })
            .OfType<string>()
            .Where(label => label.Length > 0)
            .ToList();
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Int64(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static DateTimeOffset? Date(JsonElement element, string name)
        => String(element, name) is { } text
           && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
}
