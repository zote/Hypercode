using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>Por onde a issue foi ligada ao worktree: o PR declara que a fecha, ou o número está no nome da branch.</summary>
public enum IssueLinkSource { PullRequest, Branch }

/// <summary>
/// O worktree que já trata uma issue do grafo (#148). <paramref name="Others"/>: outros
/// worktrees que apontam para a mesma issue — o menu age em <paramref name="Row"/>, e o tooltip
/// avisa que há mais.
/// </summary>
public sealed record IssueWorktreeLink(WorktreeRow Row, IssueLinkSource Source, IReadOnlyList<WorktreeRow> Others)
{
    /// <summary>De onde veio a ligação, para quem olha saber se é declarada ou palpite.</summary>
    public string Origin => Source == IssueLinkSource.PullRequest && Row.PullRequest is { } pullRequest
        ? $"pelo PR #{pullRequest.Number}"
        : "pelo nome da branch";

    public string Tooltip
    {
        get
        {
            var text = $"Já tem worktree, {Origin}: {Row.Name} ({Row.Branch}).";
            if (Others.Count > 0)
                text += $" Há mais {(Others.Count == 1 ? "1 worktree" : $"{Others.Count} worktrees")} para ela: "
                        + string.Join(", ", Others.Select(other => other.Name)) + " — o menu age neste.";
            return text + " Botão direito: o mesmo menu da lista de worktrees.";
        }
    }

    /// <summary>
    /// Liga as issues de <paramref name="repository"/> (owner/repo) aos worktrees. Duas fontes,
    /// nessa ordem: as issues que o PR do worktree declara fechar e, se ele não declara nenhuma
    /// (ou não tem PR), o número no nome da branch. Se o PR declara, a branch não conta — mesmo
    /// que aponte para outra issue: vence a declarada. Issue de outro repositório nunca liga.
    /// Mais de um worktree para a mesma issue: fica o ligado pelo PR, depois o de PR aberto,
    /// depois o de PR mais novo; os outros vão em <see cref="Others"/>.
    /// </summary>
    public static IReadOnlyDictionary<IssueKey, IssueWorktreeLink> Build(string repository, IEnumerable<WorktreeRow> rows)
    {
        var candidates = new Dictionary<IssueKey, List<(WorktreeRow Row, IssueLinkSource Source)>>();

        foreach (var row in rows)
        {
            if (row.Worktree.IsBare) continue;

            foreach (var (key, source) in IssuesOf(repository, row))
            {
                if (!candidates.TryGetValue(key, out var list)) candidates[key] = list = new();
                list.Add((row, source));
            }
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var ordered = pair.Value
                    .OrderBy(candidate => candidate.Source)
                    .ThenByDescending(candidate => candidate.Row.PullRequest?.Relevance ?? 0)
                    .ThenByDescending(candidate => candidate.Row.PullRequest?.Number ?? 0)
                    .ThenBy(candidate => candidate.Row.Name, StringComparer.Ordinal)
                    .ToList();
                return new IssueWorktreeLink(ordered[0].Row, ordered[0].Source, ordered.Skip(1).Select(candidate => candidate.Row).ToList());
            });
    }

    private static IEnumerable<(IssueKey Key, IssueLinkSource Source)> IssuesOf(string repository, WorktreeRow row)
    {
        var local = new IssueKey(repository, 0).Repository;

        if (row.PullRequest?.ClosingIssues is { Count: > 0 } declared)
            return declared.Where(key => key.Repository == local).Select(key => (key, IssueLinkSource.PullRequest));

        var number = IssueBranch.Number(row.Worktree.TrackedBranch) ?? IssueBranch.Number(row.Worktree.UpstreamBranch);
        return number is { } found
            ? new[] { (new IssueKey(repository, found), IssueLinkSource.Branch) }
            : Array.Empty<(IssueKey, IssueLinkSource)>();
    }
}
