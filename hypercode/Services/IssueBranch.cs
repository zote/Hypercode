using System.Text.RegularExpressions;

namespace Hypercode.Services;

/// <summary>
/// O caminho inverso do <see cref="WorktreeCreator.IssueBranchName"/>: qual issue uma branch
/// trata, pelo nome (#148). É palpite — a fonte boa é o closingIssuesReferences do PR —, mas
/// cobre PR sem palavra-chave de fechamento e worktree que ainda nem tem PR.
/// </summary>
public static partial class IssueBranch
{
    /// <summary>
    /// O número de <c>issue-&lt;n&gt;</c> em qualquer ponto da branch (<c>claude/issue-1269-…</c>)
    /// ou, sem ele, o que vem logo depois da primeira barra (<c>feat/880-…</c>,
    /// <c>codex/966-…</c>). Null quando nenhum dos dois aparece.
    /// </summary>
    public static int? Number(string? branch)
    {
        if (string.IsNullOrEmpty(branch)) return null;

        var match = IssuePrefixPattern().Match(branch);
        if (!match.Success) match = AfterFirstSlashPattern().Match(branch);

        return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0 ? number : null;
    }

    [GeneratedRegex(@"(?:^|[/_-])issue-(\d+)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex IssuePrefixPattern();

    [GeneratedRegex(@"^[^/]+/(\d+)(?=-|$)")]
    private static partial Regex AfterFirstSlashPattern();
}
