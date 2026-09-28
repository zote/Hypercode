using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypertree.Services;

public enum ChecksState { None, Pending, Failing, Passing }

public enum ReviewState { None, Required, ChangesRequested, Approved }

public sealed class CheckEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("context")] public string? Context { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("conclusion")] public string? Conclusion { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }

    public string Label => Name ?? Context ?? "check";
}

public sealed class PullRequestInfo
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("headRefName")] public string HeadRefName { get; set; } = string.Empty;

    /// <summary>Branch em que o PR vai entrar — nem sempre a main: PRs empilhados apontam para outra feature.</summary>
    [JsonPropertyName("baseRefName")] public string? BaseRefName { get; set; }

    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
    [JsonPropertyName("mergeable")] public string? Mergeable { get; set; }
    [JsonPropertyName("mergeStateStatus")] public string? MergeStateStatus { get; set; }
    [JsonPropertyName("reviewDecision")] public string? ReviewDecision { get; set; }
    [JsonPropertyName("statusCheckRollup")] public List<CheckEntry>? StatusCheckRollup { get; set; }

    public bool IsOpen => State.Equals("OPEN", StringComparison.OrdinalIgnoreCase);

    /// <summary>OPEN &gt; MERGED &gt; CLOSED, para escolher o PR mais relevante de uma branch.</summary>
    public int Relevance => State.ToUpperInvariant() switch
    {
        "OPEN" => 3,
        "MERGED" => 2,
        _ => 1,
    };

    /// <summary>Conflito com a base — precisa rebase ou merge antes de integrar.</summary>
    public bool HasConflicts =>
        IsOpen && string.Equals(Mergeable, "CONFLICTING", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A base andou desde que a branch saiu; o GitHub pede atualização. Quem resolve é trazer
    /// a base para dentro da branch (merge ou rebase) — não um pull do upstream da própria branch.
    /// </summary>
    public bool IsBehindBase =>
        IsOpen && string.Equals(MergeStateStatus, "BEHIND", StringComparison.OrdinalIgnoreCase);

    public ReviewState Review => ReviewDecision?.ToUpperInvariant() switch
    {
        "APPROVED" => ReviewState.Approved,
        "CHANGES_REQUESTED" => ReviewState.ChangesRequested,
        "REVIEW_REQUIRED" => ReviewState.Required,
        _ => ReviewState.None,
    };

    public ChecksState Checks
    {
        get
        {
            if (StatusCheckRollup is not { Count: > 0 }) return ChecksState.None;

            var pending = false;

            foreach (var entry in StatusCheckRollup)
            {
                var conclusion = entry.Conclusion?.ToUpperInvariant();
                var state = entry.State?.ToUpperInvariant();
                var status = entry.Status?.ToUpperInvariant();

                if (conclusion is "FAILURE" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE"
                    || state is "FAILURE" or "ERROR")
                    return ChecksState.Failing;

                if (state is "PENDING" or "EXPECTED") pending = true;
                else if (status is not null && status != "COMPLETED") pending = true;
                else if (conclusion is null && state is null) pending = true;
            }

            return pending ? ChecksState.Pending : ChecksState.Passing;
        }
    }

    public string FailingChecksSummary => StatusCheckRollup is null
        ? string.Empty
        : string.Join(", ", StatusCheckRollup
            .Where(entry =>
                entry.Conclusion?.ToUpperInvariant() is "FAILURE" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE"
                || entry.State?.ToUpperInvariant() is "FAILURE" or "ERROR")
            .Select(entry => entry.Label)
            .Take(5));
}

/// <summary>O mínimo de um PR para criar um worktree a partir dele.</summary>
public sealed class PullRequestHead
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("headRefName")] public string HeadRefName { get; set; } = string.Empty;
    [JsonPropertyName("isCrossRepository")] public bool IsCrossRepository { get; set; }
}

/// <param name="Failed">
/// A consulta não trouxe resposta (sem gh, sem autenticação, rede). Diferente de "não há PRs":
/// quem já tem PRs na tela não deve apagá-los por causa disso.
/// </param>
public sealed record PullRequestLookup(
    IReadOnlyDictionary<string, PullRequestInfo> ByBranch,
    string? Warning,
    bool Failed = false)
{
    public static PullRequestLookup Empty(string? warning = null) =>
        new(new Dictionary<string, PullRequestInfo>(StringComparer.OrdinalIgnoreCase), warning);

    public static PullRequestLookup Failure(string warning) => Empty(warning) with { Failed = true };
}

public static class GitHubService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string RichFields =
        "number,headRefName,baseRefName,state,title,url,isDraft,mergeable,mergeStateStatus,reviewDecision,statusCheckRollup";

    private const string BasicFields = "number,headRefName,baseRefName,state,title,url,isDraft";

    /// <summary>Branches por consulta GraphQL — folga grande no limite de nós do GitHub.</summary>
    private const int BranchesPerQuery = 40;

    private const string PullRequestFragment = """
        fragment F on PullRequest {
          number headRefName baseRefName state title url isDraft mergeable mergeStateStatus reviewDecision
          commits(last: 1) { nodes { commit { statusCheckRollup { contexts(first: 100) { nodes {
            __typename
            ... on CheckRun { name status conclusion }
            ... on StatusContext { context state }
          } } } } } }
        }
        """;

    /// <summary>
    /// PRs das <paramref name="branches"/> (as dos worktrees). Primeiro numa consulta GraphQL
    /// só, com um alias por branch — não depende de o PR estar entre os 100 mais recentes do
    /// repositório. Se ela falhar, cai para o `gh pr list` dos 100 mais recentes.
    /// </summary>
    public static async Task<PullRequestLookup> LoadPullRequestsAsync(
        string repositoryPath,
        IReadOnlyCollection<string> branches,
        CancellationToken cancellationToken = default)
    {
        var gh = ExecutableLocator.Find("gh");
        if (gh is null)
            return PullRequestLookup.Failure("GitHub CLI (gh) não encontrado — a coluna PR fica vazia. Instale com: brew install gh");

        if (branches.Count > 0
            && await TryLoadByBranchAsync(gh, repositoryPath, branches, cancellationToken).ConfigureAwait(false) is { } found)
            return new PullRequestLookup(found, null);

        // Campos como mergeStateStatus e statusCheckRollup podem não existir em versões
        // antigas do gh; se a consulta rica falhar, caímos para o conjunto básico.
        var result = await TryListAsync(gh, repositoryPath, RichFields, cancellationToken).ConfigureAwait(false);
        string? degraded = null;

        if (result is null || !result.Success)
        {
            result = await TryListAsync(gh, repositoryPath, BasicFields, cancellationToken).ConfigureAwait(false);
            degraded = "sem checks/review (gh antigo?)";
        }

        if (result is null)
            return PullRequestLookup.Failure("gh pr list não respondeu a tempo.");

        if (!result.Success)
            return PullRequestLookup.Failure($"gh pr list falhou: {result.FirstErrorLine}");

        List<PullRequestInfo>? pullRequests;
        try
        {
            pullRequests = JsonSerializer.Deserialize<List<PullRequestInfo>>(result.StandardOutput, JsonOptions);
        }
        catch (JsonException exception)
        {
            return PullRequestLookup.Failure($"Não consegui ler a resposta do gh: {exception.Message}");
        }

        if (pullRequests is null || pullRequests.Count == 0)
            return PullRequestLookup.Empty(degraded);

        var byBranch = new Dictionary<string, PullRequestInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var pullRequest in pullRequests) Keep(byBranch, pullRequest);

        return new PullRequestLookup(byBranch, degraded);
    }

    /// <summary>Havendo mais de um PR para a branch, fica o aberto; depois o merged; depois o mais novo.</summary>
    private static void Keep(Dictionary<string, PullRequestInfo> byBranch, PullRequestInfo pullRequest)
    {
        if (string.IsNullOrEmpty(pullRequest.HeadRefName)) return;

        if (!byBranch.TryGetValue(pullRequest.HeadRefName, out var existing)
            || pullRequest.Relevance > existing.Relevance
            || (pullRequest.Relevance == existing.Relevance && pullRequest.Number > existing.Number))
        {
            byBranch[pullRequest.HeadRefName] = pullRequest;
        }
    }

    /// <summary>
    /// `gh api graphql` com um alias por branch; os nomes vão como variáveis, sem escapar nada
    /// na consulta. {owner} e {repo} o próprio gh resolve pelo remoto do diretório. Custa 1
    /// ponto do rate limit por consulta. Null em qualquer falha — quem chama cai para o pr list.
    /// </summary>
    private static async Task<Dictionary<string, PullRequestInfo>?> TryLoadByBranchAsync(
        string gh,
        string repositoryPath,
        IReadOnlyCollection<string> branches,
        CancellationToken cancellationToken)
    {
        var byBranch = new Dictionary<string, PullRequestInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in branches.Distinct(StringComparer.Ordinal).Chunk(BranchesPerQuery))
        {
            var arguments = new List<string>
            {
                "api", "graphql",
                // mergeStateStatus ainda pede o preview na API; o gh pr list manda o mesmo cabeçalho.
                "-H", "Accept: application/vnd.github.merge-info-preview+json",
                "-F", "owner={owner}", "-F", "name={repo}",
            };

            var variables = new List<string> { "$owner: String!", "$name: String!" };
            var fields = new List<string>();

            for (var index = 0; index < chunk.Length; index++)
            {
                arguments.Add("-f");
                arguments.Add($"b{index}={chunk[index]}");
                variables.Add($"$b{index}: String!");
                fields.Add($"b{index}: pullRequests(headRefName: $b{index}, first: 5, orderBy: {{field: CREATED_AT, direction: DESC}}) {{ nodes {{ ...F }} }}");
            }

            arguments.Add("-f");
            arguments.Add($"query=query({string.Join(", ", variables)}) {{ repository(owner: $owner, name: $name) {{ {string.Join(" ", fields)} }} }}\n{PullRequestFragment}");

            ProcessResult result;
            try
            {
                result = await ProcessRunner.RunAsync(gh, arguments, repositoryPath, TimeSpan.FromSeconds(60), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }

            if (!result.Success) return null;

            try
            {
                using var document = JsonDocument.Parse(result.StandardOutput);
                if (!document.RootElement.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("repository", out var repository)
                    || repository.ValueKind != JsonValueKind.Object)
                    return null;

                foreach (var alias in repository.EnumerateObject())
                {
                    if (!alias.Value.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) continue;
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (ParseGraphNode(node) is { } pullRequest) Keep(byBranch, pullRequest);
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return byBranch;
    }

    /// <summary>
    /// Nó de PR do GraphQL → o mesmo <see cref="PullRequestInfo"/> do pr list. Os checks vêm
    /// de commits.last.statusCheckRollup.contexts: CheckRun (Actions) tem name/status/conclusion,
    /// StatusContext (status API antiga) tem context/state — os nomes que o pr list já usa.
    /// </summary>
    internal static PullRequestInfo? ParseGraphNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;

        var pullRequest = node.Deserialize<PullRequestInfo>(JsonOptions);
        if (pullRequest is null) return null;

        if (node.TryGetProperty("commits", out var commits)
            && commits.TryGetProperty("nodes", out var commitNodes)
            && commitNodes.ValueKind == JsonValueKind.Array
            && commitNodes.GetArrayLength() > 0
            && commitNodes[0].TryGetProperty("commit", out var commit)
            && commit.TryGetProperty("statusCheckRollup", out var rollup)
            && rollup.ValueKind == JsonValueKind.Object
            && rollup.TryGetProperty("contexts", out var contexts)
            && contexts.TryGetProperty("nodes", out var contextNodes)
            && contextNodes.ValueKind == JsonValueKind.Array)
        {
            pullRequest.StatusCheckRollup = contextNodes
                .EnumerateArray()
                .Where(context => context.ValueKind == JsonValueKind.Object)
                .Select(context => context.Deserialize<CheckEntry>(JsonOptions))
                .OfType<CheckEntry>()
                .ToList();
        }

        return pullRequest;
    }

    /// <summary>Busca um PR pelo número. Lança com a mensagem do gh quando não acha.</summary>
    public static async Task<PullRequestHead> GetPullRequestHeadAsync(
        string repositoryPath,
        int number,
        CancellationToken cancellationToken = default)
    {
        var gh = ExecutableLocator.Find("gh")
            ?? throw new InvalidOperationException("GitHub CLI (gh) não encontrado. Instale com: brew install gh");

        var result = await ProcessRunner.RunAsync(
            gh,
            new[] { "pr", "view", number.ToString(CultureInfo.InvariantCulture), "--json", "number,title,state,headRefName,isCrossRepository" },
            repositoryPath,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException($"PR #{number}: {result.FirstErrorLine}");

        return JsonSerializer.Deserialize<PullRequestHead>(result.StandardOutput, JsonOptions)
            ?? throw new InvalidOperationException($"PR #{number}: resposta vazia do gh.");
    }

    private static async Task<ProcessResult?> TryListAsync(
        string gh,
        string repositoryPath,
        string fields,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessRunner.RunAsync(
                gh,
                new[] { "pr", "list", "--state", "all", "--limit", "100", "--json", fields },
                repositoryPath,
                TimeSpan.FromSeconds(60),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
