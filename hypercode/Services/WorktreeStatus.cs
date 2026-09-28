namespace Hypercode.Services;

/// <summary>Estado de trabalho de um worktree, lido com `git status --porcelain=v2 --branch`.</summary>
public sealed record WorktreeStatus
{
    public static readonly WorktreeStatus Unknown = new();

    public bool IsKnown { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public bool HasUnmergedPaths { get; init; }
    public bool HasUpstream { get; init; }

    /// <summary>
    /// A branch rastreia uma remota que não existe mais — típico depois do merge do PR, quando
    /// o GitHub apaga a branch e um fetch --prune remove a ref. O git ainda lista o upstream,
    /// mas sem contagem ahead/behind.
    /// </summary>
    public bool IsUpstreamGone { get; init; }

    /// <summary>
    /// Com upstream "gone", a ref remota da base (ex.: origin/main) quando o HEAD já está contido
    /// nela (`git merge-base --is-ancestor`). Null se o upstream existe, se não há base remota ou
    /// se o HEAD tem commit que a base não tem — squash e rebase merge caem aqui, porque reescrevem
    /// os commits.
    /// </summary>
    public string? ContainedInBase { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }

    /// <summary>"merge", "rebase", "cherry-pick", "revert" ou "bisect" — null se nada em andamento.</summary>
    public string? PendingOperation { get; init; }

    public bool IsDiverged => Ahead > 0 && Behind > 0;
    public bool NeedsPush => Ahead > 0 || (IsKnown && !HasUpstream);
    public bool NeedsPull => Behind > 0;
}

public static class WorktreeStatusReader
{
    public static async Task<WorktreeStatus> ReadAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(worktreePath)) return WorktreeStatus.Unknown;

        var git = ExecutableLocator.Find("git");
        if (git is null) return WorktreeStatus.Unknown;

        try
        {
            var statusTask = ProcessRunner.RunAsync(
                git,
                new[] { "-C", worktreePath, "status", "--porcelain=v2", "--branch" },
                worktreePath, TimeSpan.FromSeconds(25), cancellationToken);

            var gitDirTask = ProcessRunner.RunAsync(
                git,
                new[] { "-C", worktreePath, "rev-parse", "--absolute-git-dir" },
                worktreePath, TimeSpan.FromSeconds(15), cancellationToken);

            await Task.WhenAll(statusTask, gitDirTask).ConfigureAwait(false);

            var status = await statusTask.ConfigureAwait(false);
            if (!status.Success) return WorktreeStatus.Unknown;

            var gitDirResult = await gitDirTask.ConfigureAwait(false);
            var gitDir = gitDirResult.Success ? gitDirResult.StandardOutput.Trim() : null;

            var parsed = Parse(status.StandardOutput, DetectPendingOperation(gitDir));
            if (!parsed.IsUpstreamGone || ParseBranchHead(status.StandardOutput) is not { } branch) return parsed;

            return parsed with
            {
                ContainedInBase = await FindContainingBaseAsync(worktreePath, branch, cancellationToken)
                    .ConfigureAwait(false),
            };
        }
        catch (Exception)
        {
            // Status é enfeite: se falhar, a linha só fica sem ícones.
            return WorktreeStatus.Unknown;
        }
    }

    /// <summary>
    /// A base remota default (origin/HEAD, caindo para origin/main e origin/master) se ela já
    /// contém o HEAD. Só vale ref remota: o último recurso do ResolveDefaultBaseAsync é a branch
    /// local do worktree, que conteria o próprio HEAD e liberaria qualquer coisa para a limpeza.
    /// </summary>
    private static async Task<string?> FindContainingBaseAsync(
        string worktreePath,
        string branch,
        CancellationToken cancellationToken)
    {
        var remote = await GitService.ResolveRemoteAsync(worktreePath, branch, cancellationToken).ConfigureAwait(false);
        var baseRef = await GitService.ResolveDefaultBaseAsync(worktreePath, remote, cancellationToken).ConfigureAwait(false);
        if (baseRef is null || !baseRef.StartsWith($"{remote}/", StringComparison.Ordinal)) return null;

        var ancestor = await GitService.RunAsync(
            worktreePath,
            new[] { "merge-base", "--is-ancestor", "HEAD", baseRef },
            TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        return ancestor.Success ? baseRef : null;
    }

    /// <summary>Nome da branch em "# branch.head" — null em detached HEAD.</summary>
    internal static string? ParseBranchHead(string porcelainV2)
    {
        const string prefix = "# branch.head ";
        foreach (var rawLine in porcelainV2.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var head = line[prefix.Length..];
            return head is "(detached)" or "" ? null : head;
        }

        return null;
    }

    internal static string? DetectPendingOperation(string? gitDir)
    {
        if (string.IsNullOrEmpty(gitDir) || !Directory.Exists(gitDir)) return null;

        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return "rebase";
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return "merge";
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return "cherry-pick";
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) return "revert";
        if (File.Exists(Path.Combine(gitDir, "BISECT_LOG"))) return "bisect";

        return null;
    }

    /// <summary>
    /// Formato v2: linhas de cabeçalho começam com '#'; qualquer outra linha
    /// (1, 2, u, ?, !) é uma mudança na árvore de trabalho. Upstream sem "# branch.ab" é
    /// upstream que sumiu do remoto.
    /// </summary>
    internal static WorktreeStatus Parse(string porcelainV2, string? pendingOperation)
    {
        var hasChanges = false;
        var hasUnmerged = false;
        var hasUpstream = false;
        var hasCounts = false;
        var ahead = 0;
        var behind = 0;

        foreach (var rawLine in porcelainV2.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0) continue;

            if (line[0] != '#')
            {
                hasChanges = true;
                if (line[0] == 'u') hasUnmerged = true;
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                hasUpstream = true;
            }
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                hasUpstream = true;
                hasCounts = true;
                foreach (var token in line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length < 2) continue;
                    if (!int.TryParse(token[1..], out var value)) continue;
                    if (token[0] == '+') ahead = value;
                    else if (token[0] == '-') behind = value;
                }
            }
        }

        return new WorktreeStatus
        {
            IsKnown = true,
            HasUncommittedChanges = hasChanges,
            HasUnmergedPaths = hasUnmerged,
            HasUpstream = hasUpstream,
            IsUpstreamGone = hasUpstream && !hasCounts,
            Ahead = ahead,
            Behind = behind,
            PendingOperation = pendingOperation,
        };
    }
}
