using System.Globalization;
using System.Text.Json;

namespace Hypercode.Services;

/// <summary>
/// Uma resposta da API REST pelo <c>gh api -i</c>: o status HTTP, o corpo e a cota lida dos
/// cabeçalhos. Status 0 é falha antes de chegar ao GitHub (sem gh, sem rede, sem login).
/// <see cref="Scopes"/> é o <c>X-OAuth-Scopes</c>: null quando o token não o manda (fine-grained).
/// </summary>
public sealed record ApiResponse(int Status, string Body, ApiBudget? Budget, string? Error, string? Scopes = null)
{
    public bool Success => Status is >= 200 and < 300;

    /// <summary>
    /// Separa status, cabeçalhos e corpo da saída do <c>gh api -i</c>. O gh sai com código 1 em
    /// 4xx e 5xx, mas a saída tem o status do mesmo jeito — é ele que distingue 403 de 404.
    /// </summary>
    public static ApiResponse Parse(string output, string? error)
    {
        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        var split = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var head = split < 0 ? normalized : normalized[..split];
        var body = split < 0 ? string.Empty : normalized[(split + 2)..];

        var lines = head.Split('\n');
        if (lines.Length == 0
            || !lines[0].StartsWith("HTTP/", StringComparison.Ordinal)
            || lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [_, var code, ..]
            || !int.TryParse(code, out var status))
            return new ApiResponse(0, string.Empty, null, error);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return new ApiResponse(
            status,
            body,
            ParseBudget(headers),
            status is >= 200 and < 300 ? null : error,
            headers.TryGetValue("X-OAuth-Scopes", out var scopes) ? scopes : null);
    }

    private static ApiBudget? ParseBudget(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-RateLimit-Limit", out var limit) || !int.TryParse(limit, out var total)
            || !headers.TryGetValue("X-RateLimit-Used", out var used) || !int.TryParse(used, out var spent)
            || !headers.TryGetValue("X-RateLimit-Reset", out var reset)
            || !long.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            return null;

        return new ApiBudget(total, spent, 1, DateTimeOffset.FromUnixTimeSeconds(epoch));
    }
}

/// <summary>
/// Lê do GitHub o que o painel da fila mostra. Por ciclo, por repositório acompanhado: os runs
/// em <c>queued</c> e em <c>in_progress</c>, os jobs de cada um e, se algo espera, os grupos de
/// <c>concurrency</c> ativos com os membros de cada um. Mais os runners, por dono: a
/// lista de grupos e os runners de cada grupo — a lista da org não diz mais o grupo de cada um.
/// Fora do ciclo, de hora em hora, o histórico de durações que a estimativa de início usa.
/// </summary>
public static class ActionsQueueService
{
    /// <summary>Chamadas em paralelo: o bastante para o ciclo não levar segundos, pouco para não virar rajada.</summary>
    private const int Parallelism = 4;

    public const string PermissionHint =
        "o token do gh precisa ler os runners da organização (escopo admin:org — gh auth refresh -s admin:org)";

    /// <summary>
    /// Um GET pelo <c>gh api -i</c>. Nunca lança por HTTP: 403 e 404 voltam como status, que
    /// é o que separa sem permissão de não é organização.
    /// </summary>
    public static Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken)
        => SendAsync(new[] { "api", "-i", path }, cancellationToken);

    /// <summary>Um POST sem corpo pelo <c>gh api -i</c> — cancelar um run é só isso. Também não lança por HTTP.</summary>
    public static Task<ApiResponse> PostAsync(string path, CancellationToken cancellationToken)
        => SendAsync(new[] { "api", "-i", "-X", "POST", path }, cancellationToken);

    private static async Task<ApiResponse> SendAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (ExecutableLocator.Find("gh") is not { } gh)
            return new ApiResponse(0, string.Empty, null, "GitHub CLI (gh) não encontrado. Instale com: brew install gh");

        try
        {
            var result = await ProcessRunner.RunAsync(gh, arguments, null, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ApiResponse.Parse(result.StandardOutput, result.FirstErrorLine);
        }
        catch (TimeoutException)
        {
            return new ApiResponse(0, string.Empty, null, "o GitHub não respondeu a tempo");
        }
    }

    /// <summary>
    /// Um ciclo inteiro. Runners e fila falham separados: sem admin na org, os runners viram uma
    /// mensagem e a fila — que só precisa ler os repositórios — continua.
    /// </summary>
    public static async Task<ActionsSnapshot> LoadAsync(
        IReadOnlyList<string> repositories,
        Func<string, CancellationToken, Task<ApiResponse>> get,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(Parallelism);
        var budgets = new List<ApiBudget>();

        async Task<ApiResponse> Get(string path)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await get(path, cancellationToken).ConfigureAwait(false);
                if (response.Budget is { } budget)
                    lock (budgets) budgets.Add(budget);
                return response;
            }
            finally
            {
                gate.Release();
            }
        }

        var queue = Task.WhenAll(repositories.Select(repository => LoadRepositoryAsync(repository, Get)));
        var runners = Task.WhenAll(repositories
            .GroupBy(Owner, StringComparer.OrdinalIgnoreCase)
            .Select(owner => LoadRunnersAsync(owner.Key, owner.ToList(), Get)));

        var queueResults = await queue.ConfigureAwait(false);
        var runnerResults = await runners.ConfigureAwait(false);

        // A cota mais recente: a do reset mais adiante e, nele, a do maior consumo.
        var latest = budgets.OrderBy(budget => budget.ResetAt).ThenBy(budget => budget.Used).LastOrDefault();

        return new ActionsSnapshot(
            runnerResults.SelectMany(result => result.Runners).ToList(),
            queueResults.SelectMany(result => result.Jobs).ToList(),
            queueResults.SelectMany(result => result.Waiting).ToList(),
            queueResults.SelectMany(result => result.Runs).ToList(),
            runnerResults.Select(result => result.Problem).OfType<string>().ToList(),
            queueResults.Select(result => result.Problem).OfType<string>().ToList(),
            latest,
            now)
        {
            Holds = queueResults.SelectMany(result => result.Holds).ToList(),
        };
    }

    /// <summary>
    /// O histórico de um repositório: os workflows ativos, os últimos
    /// <see cref="ActionsQueue.HistoryRuns"/> runs concluídos de cada e os jobs de cada run —
    /// uma chamada por run. Por isso anda de hora em hora, nunca na cadência do painel. Null se
    /// a lista de workflows falhar; run cujos jobs falharem só fica de fora.
    /// </summary>
    public static async Task<RepositoryDurations?> LoadDurationsAsync(
        string repository,
        Func<string, CancellationToken, Task<ApiResponse>> get,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(Parallelism);

        async Task<JsonDocument?> Get(string path)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await get(path, cancellationToken).ConfigureAwait(false);
                return response.Success ? TryParse(response.Body) : null;
            }
            finally
            {
                gate.Release();
            }
        }

        IReadOnlyList<long> workflows;
        using (var document = await Get($"repos/{repository}/actions/workflows?per_page=100").ConfigureAwait(false))
        {
            if (document is null) return null;
            workflows = ActionsQueue.ParseWorkflows(document.RootElement);
        }

        var runs = (await Task.WhenAll(workflows.Select(async workflow =>
        {
            using var document = await Get($"repos/{repository}/actions/workflows/{workflow}/runs?status=completed&per_page={ActionsQueue.HistoryRuns}")
                .ConfigureAwait(false);
            return document is null ? Array.Empty<ActionsQueue.RunInfo>() : ActionsQueue.ParseRuns(document.RootElement);
        })).ConfigureAwait(false)).SelectMany(list => list).ToList();

        var samples = (await Task.WhenAll(runs.Select(async run =>
        {
            using var document = await Get($"repos/{repository}/actions/runs/{run.Id}/jobs?per_page=100").ConfigureAwait(false);
            return document is null
                ? Array.Empty<(string, string, IReadOnlyList<string>, TimeSpan)>()
                : ActionsQueue.ParseJobDurations(document.RootElement, run.Workflow);
        })).ConfigureAwait(false)).SelectMany(list => list);

        return new RepositoryDurations { CollectedAt = now, Jobs = ActionsQueue.Medians(samples) };
    }

    private static string Owner(string repository) => repository.Split('/')[0];

    private sealed record RepositoryResult(IReadOnlyList<ActionsJob> Jobs, IReadOnlyList<ActionsWaitingRun> Waiting, IReadOnlyList<ActionsRun> Runs, string? Problem)
    {
        public IReadOnlyList<ActionsHold> Holds { get; init; } = Array.Empty<ActionsHold>();
    }

    private sealed record RunnerResult(IReadOnlyList<ActionsRunner> Runners, string? Problem);

    private static async Task<RepositoryResult> LoadRepositoryAsync(string repository, Func<string, Task<ApiResponse>> get)
    {
        var responses = await Task.WhenAll(
            get($"repos/{repository}/actions/runs?status=queued&per_page=100"),
            get($"repos/{repository}/actions/runs?status=in_progress&per_page=100")).ConfigureAwait(false);

        if (responses.FirstOrDefault(response => !response.Success) is { } failed)
            return new RepositoryResult(Array.Empty<ActionsJob>(), Array.Empty<ActionsWaitingRun>(), Array.Empty<ActionsRun>(), $"{repository}: {Describe(failed)}");

        var runs = new List<ActionsQueue.RunInfo>();
        foreach (var response in responses)
            if (TryParse(response.Body) is { } document)
                using (document)
                    runs.AddRange(ActionsQueue.ParseRuns(document.RootElement));

        // Um run pode trocar de queued para in_progress entre as duas chamadas.
        runs = runs.DistinctBy(run => run.Id).ToList();

        var perRun = await Task.WhenAll(runs.Select(async run =>
        {
            var response = await get($"repos/{repository}/actions/runs/{run.Id}/jobs?per_page=100").ConfigureAwait(false);

            // Run que terminou entre as chamadas some: 404 não é erro, é só um run a menos.
            if (!response.Success || TryParse(response.Body) is not { } document) return (Run: run, Jobs: (IReadOnlyList<ActionsJob>?)null);
            using (document) return (Run: run, Jobs: ActionsQueue.ParseJobs(document.RootElement, repository, run));
        })).ConfigureAwait(false);

        var jobs = perRun.SelectMany(item => item.Jobs ?? Array.Empty<ActionsJob>()).ToList();
        var waiting = perRun
            .Where(item => item.Jobs is { Count: 0 } && string.Equals(item.Run.Status, "queued", StringComparison.OrdinalIgnoreCase))
            .Select(item => new ActionsWaitingRun(item.Run.Id, repository, item.Run.Workflow, item.Run.Branch, item.Run.PullRequest, item.Run.CreatedAt, item.Run.Url))
            .ToList();

        // Só há quem segurar se há quem espere: repositório sem nada na fila não gasta a chamada.
        // Job segurado por concurrency de job pode vir como pending ou waiting, não só queued.
        var holds = waiting.Count > 0 || jobs.Any(job => !job.IsRunning && !string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase))
            ? await LoadHoldsAsync(repository, get).ConfigureAwait(false)
            : Array.Empty<ActionsHold>();

        return new RepositoryResult(jobs, waiting, runs.Select(run => run.ToRun(repository)).ToList(), null) { Holds = holds };
    }

    /// <summary>
    /// Quem a <c>concurrency</c> segura num repositório (#131): a listagem dos grupos ativos e o
    /// GET de cada um, que é o que traz os membros. O endpoint por run não serve — devolveu zero
    /// grupos para um run que era membro de um. Falhar aqui não é problema da fila: sem saber, o
    /// painel só não distingue, como antes.
    /// </summary>
    private static async Task<IReadOnlyList<ActionsHold>> LoadHoldsAsync(string repository, Func<string, Task<ApiResponse>> get)
    {
        var list = await get($"repos/{repository}/actions/concurrency_groups?per_page=100").ConfigureAwait(false);
        if (!list.Success || TryParse(list.Body) is not { } listDocument) return Array.Empty<ActionsHold>();

        IReadOnlyList<(string Name, string Path)> groups;
        using (listDocument) groups = ActionsQueue.ParseConcurrencyGroups(listDocument.RootElement, repository);

        var loaded = await Task.WhenAll(groups.Select(async group =>
        {
            // Grupo que esvaziou entre as chamadas dá 404: é só um grupo a menos.
            var response = await get(group.Path).ConfigureAwait(false);
            if (!response.Success || TryParse(response.Body) is not { } document) return null;
            using (document) return ActionsQueue.ParseConcurrencyGroup(document.RootElement, repository, group.Name);
        })).ConfigureAwait(false);

        return ActionsQueue.Holds(loaded.OfType<ActionsConcurrencyGroup>());
    }

    /// <summary>
    /// Os runners de um dono. Organização: os grupos e os runners de cada um. 404 nos grupos é
    /// conta pessoal (ou org sem acesso): ficam os runners de cada repositório dela.
    /// </summary>
    private static async Task<RunnerResult> LoadRunnersAsync(string owner, IReadOnlyList<string> repositories, Func<string, Task<ApiResponse>> get)
    {
        var groups = await get($"orgs/{owner}/actions/runner-groups?per_page=100").ConfigureAwait(false);

        if (groups.Status == 404)
        {
            var perRepository = await Task.WhenAll(repositories.Select(async repository =>
            {
                var response = await get($"repos/{repository}/actions/runners?per_page=100").ConfigureAwait(false);
                return (Repository: repository, Response: response);
            })).ConfigureAwait(false);

            var runners = new List<ActionsRunner>();
            string? problem = null;
            foreach (var (repository, response) in perRepository)
            {
                if (!response.Success)
                {
                    problem ??= response.Status is 403 or 404
                        ? $"Sem permissão para ver os runners de {repository}: precisa ser admin do repositório."
                        : $"Runners de {repository}: {Describe(response)}";
                    continue;
                }

                if (TryParse(response.Body) is { } document)
                    using (document)
                        runners.AddRange(ActionsQueue.ParseRunners(document.RootElement, repository));
            }

            return new RunnerResult(runners, problem);
        }

        if (groups.Status is 401 or 403)
            return new RunnerResult(Array.Empty<ActionsRunner>(), $"Sem permissão para ver os runners de {owner}: {PermissionHint}.");

        if (!groups.Success || TryParse(groups.Body) is not { } groupsDocument)
            return new RunnerResult(Array.Empty<ActionsRunner>(), $"Runners de {owner}: {Describe(groups)}");

        IReadOnlyList<(long Id, string Name)> list;
        using (groupsDocument) list = ActionsQueue.ParseRunnerGroups(groupsDocument.RootElement);

        var perGroup = await Task.WhenAll(list.Select(async group =>
        {
            var response = await get($"orgs/{owner}/actions/runner-groups/{group.Id}/runners?per_page=100").ConfigureAwait(false);
            if (!response.Success || TryParse(response.Body) is not { } document)
                return (Runners: (IReadOnlyList<ActionsRunner>)Array.Empty<ActionsRunner>(), Problem: $"Runners do grupo {group.Name}: {Describe(response)}");
            using (document) return (Runners: ActionsQueue.ParseRunners(document.RootElement, group.Name), Problem: (string?)null);
        })).ConfigureAwait(false);

        return new RunnerResult(
            perGroup.SelectMany(item => item.Runners).ToList(),
            perGroup.Select(item => item.Problem).OfType<string>().FirstOrDefault());
    }

    /// <summary>
    /// Se a conta e o token podem cancelar runs, por repositório. Fica de fora o repositório cuja
    /// leitura falhou: sem saber, não se bloqueia nada — o clique diz o que houver, e o próximo
    /// ciclo pergunta de novo.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, WriteAccess>> LoadAccessAsync(
        IEnumerable<string> repositories,
        Func<string, CancellationToken, Task<ApiResponse>> get,
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(repositories.Select(async repository =>
        {
            var response = await get($"repos/{repository}", cancellationToken).ConfigureAwait(false);
            if (!response.Success || TryParse(response.Body) is not { } document) return (Repository: repository, Access: (WriteAccess?)null);
            using (document) return (Repository: repository, Access: ActionsQueue.ParseWriteAccess(document.RootElement, repository, response.Scopes));
        })).ConfigureAwait(false);

        return results
            .Where(result => result.Access is not null)
            .ToDictionary(result => result.Repository, result => result.Access!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O que dizer de um pedido de cancelamento; null é que o GitHub aceitou (202). O 409 é o
    /// caso do run que terminou entre a leitura e o clique: o GitHub recusa cancelar run concluído.
    /// </summary>
    public static string? DescribeCancel(ApiResponse response, string repository) => response.Status switch
    {
        >= 200 and < 300 => null,
        409 => "O run já terminou — não há mais o que cancelar.",
        401 or 403 => $"Sem permissão para cancelar runs de {repository}: precisa de escrita no repositório, e o token do gh, do escopo repo (gh auth refresh -s repo).",
        404 => "O GitHub não achou o run: ele pode ter sido apagado, ou o token do gh não enxerga o repositório.",
        _ => $"O GitHub recusou o cancelamento: {Describe(response)}.",
    };

    private static string Describe(ApiResponse response) => response.Status switch
    {
        0 => response.Error ?? "o gh não respondeu",
        401 => "o gh não está autenticado (gh auth login)",
        403 => "sem permissão",
        404 => "não encontrado — o nome está certo e o token do gh tem acesso?",
        _ => $"HTTP {response.Status}",
    };

    private static JsonDocument? TryParse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
