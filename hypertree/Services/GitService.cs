namespace Hypertree.Services;

public sealed record WorktreeInfo
{
    public required string FullPath { get; init; }
    public string? Branch { get; init; }
    public string? Head { get; init; }
    public bool IsDetached { get; init; }
    public bool IsBare { get; init; }
    public bool IsLocked { get; init; }
    public string? LockReason { get; init; }
    public bool IsPrunable { get; init; }
    public bool IsMain { get; init; }

    /// <summary>Nome do worktree = última pasta do caminho.</summary>
    public string Name
    {
        get
        {
            var trimmed = FullPath.TrimEnd('/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? FullPath : name;
        }
    }

    public string ShortHead => string.IsNullOrEmpty(Head) ? string.Empty : Head[..Math.Min(7, Head.Length)];
}

public sealed class GitNotFoundException : Exception
{
    public GitNotFoundException()
        : base("git não foi encontrado. Instale o Xcode Command Line Tools (xcode-select --install) ou o git via Homebrew.")
    {
    }
}

public static class GitService
{
    public static async Task<IReadOnlyList<WorktreeInfo>> ListWorktreesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        var result = await ProcessRunner.RunAsync(
            git,
            new[] { "-C", repositoryPath, "worktree", "list", "--porcelain" },
            repositoryPath,
            TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(result.FirstErrorLine);

        return ParsePorcelain(result.StandardOutput);
    }

    /// <summary>
    /// Remove um worktree vinculado. Por padrão sem --force: o git recusa quando há
    /// alterações não commitadas, e é isso que queremos (nada se perde por acidente).
    /// O --force só vem de uma confirmação explícita, e um só não passa por cima do lock.
    /// </summary>
    public static async Task<ProcessResult> RemoveWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        var arguments = new List<string> { "-C", repositoryPath, "worktree", "remove" };
        if (force) arguments.Add("--force");
        arguments.Add(worktreePath);

        return await ProcessRunner.RunAsync(
            git,
            arguments,
            repositoryPath,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Traz os commits do upstream para a branch do worktree. Só fast-forward: se a branch
    /// divergiu, o git recusa e nada muda — merge ou rebase ficam a cargo de quem usa.
    /// </summary>
    public static Task<ProcessResult> PullFastForwardAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
        => RunAsync(worktreePath, new[] { "pull", "--ff-only" }, TimeSpan.FromSeconds(90), cancellationToken);

    /// <summary>Limpa os metadados de worktrees órfãos (aqueles cuja pasta sumiu).</summary>
    public static async Task<ProcessResult> PruneAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        return await ProcessRunner.RunAsync(
            git,
            new[] { "-C", repositoryPath, "worktree", "prune" },
            repositoryPath,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Roda `git -C &lt;repo&gt; ...` e devolve o resultado sem interpretar.</summary>
    public static async Task<ProcessResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        var fullArguments = new List<string> { "-C", repositoryPath };
        fullArguments.AddRange(arguments);

        return await ProcessRunner.RunAsync(
            git,
            fullArguments,
            repositoryPath,
            timeout ?? TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Branches locais e remotas (ex.: main, origin/main), sem os ponteiros HEAD.</summary>
    public static async Task<IReadOnlyList<string>> ListBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        // %(refname) completo: o formato curto de refs/remotes/origin/HEAD vira só "origin".
        var result = await RunAsync(
            repositoryPath,
            new[] { "for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success) return Array.Empty<string>();

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(reference => !reference.EndsWith("/HEAD", StringComparison.Ordinal))
            .Select(reference => reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? reference["refs/heads/".Length..]
                : reference["refs/remotes/".Length..])
            .ToList();
    }

    public static async Task<IReadOnlyList<string>> ListRemotesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryPath, new[] { "remote" }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
    }

    /// <summary>
    /// Base padrão para uma branch nova: a branch default do remoto (origin/HEAD), caindo
    /// para origin/main, origin/master e, sem remoto, para a branch do worktree principal.
    /// </summary>
    public static async Task<string?> ResolveDefaultBaseAsync(
        string repositoryPath,
        string remote,
        CancellationToken cancellationToken = default)
    {
        var head = await RunAsync(
            repositoryPath,
            new[] { "symbolic-ref", "--quiet", "--short", $"refs/remotes/{remote}/HEAD" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (head.Success && head.StandardOutput.Trim() is { Length: > 0 } remoteHead) return remoteHead;

        foreach (var candidate in new[] { $"{remote}/main", $"{remote}/master" })
            if (await RefExistsAsync(repositoryPath, $"refs/remotes/{candidate}", cancellationToken).ConfigureAwait(false))
                return candidate;

        var current = await RunAsync(
            repositoryPath,
            new[] { "symbolic-ref", "--quiet", "--short", "HEAD" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return current.Success ? current.StandardOutput.Trim() : null;
    }

    public static async Task<bool> RefExistsAsync(
        string repositoryPath,
        string fullRef,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryPath,
            new[] { "show-ref", "--verify", "--quiet", fullRef },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Success;
    }

    /// <summary>Valida o nome de branch com as regras do próprio git (check-ref-format).</summary>
    public static async Task<bool> IsValidBranchNameAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            repositoryPath,
            new[] { "check-ref-format", "--branch", branch },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Success;
    }

    /// <summary>Raiz do repositório para exibição; devolve null se o caminho não for um repo.</summary>
    public static async Task<string?> TryResolveRepositoryRootAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git");
        if (git is null) return null;

        try
        {
            var result = await ProcessRunner.RunAsync(
                git,
                new[] { "-C", path, "rev-parse", "--show-toplevel" },
                path,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (!result.Success) return null;

            var root = result.StandardOutput.Trim();
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Git dir comum a todos os worktrees (o `.git` do principal, ou o próprio repo se bare):
    /// é onde ficam `worktrees/`, `refs/` e o HEAD do principal. Null se não for um repo.
    /// </summary>
    public static async Task<string?> TryResolveCommonGitDirAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunAsync(
                path,
                new[] { "rev-parse", "--path-format=absolute", "--git-common-dir" },
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            var gitDir = result.Success ? result.StandardOutput.Trim() : string.Empty;
            return gitDir.Length > 0 && Directory.Exists(gitDir) ? gitDir : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Interpreta a saída de `git worktree list --porcelain`. Cada bloco é separado por
    /// linha em branco e começa com `worktree &lt;caminho&gt;`.
    /// </summary>
    public static IReadOnlyList<WorktreeInfo> ParsePorcelain(string porcelain)
    {
        var worktrees = new List<WorktreeInfo>();

        string? path = null;
        string? head = null;
        string? branch = null;
        string? lockReason = null;
        var detached = false;
        var bare = false;
        var locked = false;
        var prunable = false;

        void Flush()
        {
            if (path is null) return;

            worktrees.Add(new WorktreeInfo
            {
                FullPath = path,
                Head = head,
                Branch = branch,
                IsDetached = detached,
                IsBare = bare,
                IsLocked = locked,
                LockReason = lockReason,
                IsPrunable = prunable,
                IsMain = worktrees.Count == 0,
            });

            path = null;
            head = null;
            branch = null;
            lockReason = null;
            detached = false;
            bare = false;
            locked = false;
            prunable = false;
        }

        foreach (var rawLine in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var separator = line.IndexOf(' ');
            var key = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? string.Empty : line[(separator + 1)..];

            switch (key)
            {
                case "worktree":
                    Flush();
                    path = value;
                    break;
                case "HEAD":
                    head = value;
                    break;
                case "branch":
                    branch = StripRefPrefix(value);
                    break;
                case "detached":
                    detached = true;
                    break;
                case "bare":
                    bare = true;
                    break;
                case "locked":
                    locked = true;
                    lockReason = value.Length == 0 ? null : value;
                    break;
                case "prunable":
                    prunable = true;
                    break;
            }
        }

        Flush();
        return worktrees;
    }

    private static string StripRefPrefix(string reference) =>
        reference.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? reference["refs/heads/".Length..]
            : reference;
}
