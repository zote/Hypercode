using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypercode.Services;

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

    /// <summary>
    /// Nome da branch no remoto do gh, quando o upstream mora lá (branch.&lt;local&gt;.merge sem
    /// o refs/heads/). Pode diferir de <see cref="Branch"/>: o Codex cria o worktree com um nome
    /// e dá push para outro. Continua preenchido com o upstream "gone".
    /// </summary>
    public string? UpstreamBranch { get; init; }

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

    /// <summary>
    /// Ferramentas como o supacode gravam um JSON com "owner" no motivo do lock e usam o
    /// `git worktree lock` como marcador de propriedade. Isso não é um "não mexa" do usuário.
    /// </summary>
    public string? LockOwner => ExtractLockOwner(LockReason);

    /// <summary>Lock de bookkeeping de ferramenta, e não lock manual.</summary>
    public bool IsToolLock => IsLocked && !string.IsNullOrEmpty(LockOwner);

    /// <summary>Motivo do lock em texto legível — sem o JSON cru.</summary>
    public string? LockDescription
    {
        get
        {
            if (!IsLocked) return null;
            if (LockOwner is { } owner) return $"travado por {owner}";
            return string.IsNullOrWhiteSpace(LockReason) ? null : LockReason;
        }
    }

    internal static string? ExtractLockOwner(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var text = Unquote(reason.Trim());
        if (!text.StartsWith('{')) return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("owner", out var owner)
                && owner.ValueKind == JsonValueKind.String)
            {
                var value = owner.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch (JsonException)
        {
            // JSON meio escapado pelo git ainda cai no regex abaixo.
        }

        var match = Regex.Match(text, "\"owner\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>O git às vezes devolve o motivo entre aspas e com escapes.</summary>
    internal static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

        return value;
    }
}

/// <summary>Como trazer a base para dentro da branch.</summary>
public enum BaseUpdateStrategy { Merge, Rebase }

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
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(result.FirstErrorLine);

        var worktrees = ParsePorcelain(result.StandardOutput);
        var upstreams = await ReadUpstreamBranchesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        if (upstreams.Count == 0) return worktrees;

        return worktrees
            .Select(worktree => worktree.Branch is { } branch && upstreams.TryGetValue(branch, out var upstream)
                ? worktree with { UpstreamBranch = upstream }
                : worktree)
            .ToList();
    }

    /// <summary>
    /// Branch local → nome da branch no remoto do gh, só para as que rastreiam esse remoto.
    /// Upstream em outro remoto (fork) fica de fora e continua casando pelo nome local. Falha
    /// aqui não derruba a lista: sem upstream, o PR casa pelo nome local como antes.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadUpstreamBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // %(upstream:remoteref) vem da config branch.<local>.merge, então sobrevive ao
            // upstream "gone"; e não depende de tirar "origin/" na mão (remoto pode ter "/").
            var refs = RunAsync(
                repositoryPath,
                new[] { "for-each-ref", "--format=%(refname:lstrip=2)%00%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads" },
                TimeSpan.FromSeconds(20),
                cancellationToken);
            var remotes = RunAsync(
                repositoryPath,
                new[] { "config", "--get-regexp", @"^remote\..*\.(url|gh-resolved)$" },
                TimeSpan.FromSeconds(20),
                cancellationToken);

            await Task.WhenAll(refs, remotes).ConfigureAwait(false);

            if (!refs.Result.Success || ResolveGitHubRemote(remotes.Result.StandardOutput) is not { } remote)
                return new Dictionary<string, string>();

            return ParseUpstreams(refs.Result.StandardOutput, remote);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// O remoto que o gh usa para {owner}/{repo}: o marcado pelo `gh repo set-default`
    /// (remote.&lt;nome&gt;.gh-resolved), senão upstream, github, origin nessa ordem, senão o único.
    /// Recebe a saída de `git config --get-regexp '^remote\..*\.(url|gh-resolved)$'`.
    /// </summary>
    internal static string? ResolveGitHubRemote(string config)
    {
        var names = new List<string>();

        foreach (var line in config.Replace("\r\n", "\n").Split('\n'))
        {
            var separator = line.IndexOf(' ');
            if (separator < 0 || !line.StartsWith("remote.", StringComparison.Ordinal)) continue;

            var key = line[..separator];
            var value = line[(separator + 1)..].Trim();
            var suffix = key.LastIndexOf('.');
            if (suffix <= "remote.".Length) continue;

            var name = key["remote.".Length..suffix];
            if (key.EndsWith(".gh-resolved", StringComparison.Ordinal) && value == "base") return name;
            if (!names.Contains(name)) names.Add(name);
        }

        foreach (var preferred in new[] { "upstream", "github", "origin" })
            if (names.Contains(preferred)) return preferred;

        return names.Count == 1 ? names[0] : null;
    }

    /// <summary>
    /// Interpreta `git for-each-ref --format=%(refname:lstrip=2)%00%(upstream:remotename)%00%(upstream:remoteref)`
    /// e guarda o nome remoto das branches que rastreiam <paramref name="remote"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseUpstreams(string output, string remote)
    {
        const string Heads = "refs/heads/";
        var upstreams = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = line.Split('\0');
            if (parts.Length != 3 || parts[1] != remote || !parts[2].StartsWith(Heads, StringComparison.Ordinal)) continue;

            var name = parts[2][Heads.Length..];
            if (parts[0].Length > 0 && name.Length > 0) upstreams[parts[0]] = name;
        }

        return upstreams;
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
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Traz os commits do upstream para a branch do worktree. Só fast-forward: se a branch
    /// divergiu, o git recusa e nada muda — merge ou rebase ficam a cargo de quem usa.
    /// </summary>
    public static Task<ProcessResult> PullFastForwardAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
        => RunAsync(worktreePath, new[] { "pull", "--ff-only" }, TimeSpan.FromSeconds(90), cancellationToken);

    /// <summary>
    /// Metade de rede do pull em lote: atualiza as refs remotas uma vez só. Vários `git pull`
    /// em paralelo no mesmo repositório disputam o lock dessas refs e falham com "cannot lock ref".
    /// </summary>
    public static Task<ProcessResult> FetchRemoteAsync(
        string repositoryPath,
        string remote,
        CancellationToken cancellationToken = default)
        => RunAsync(repositoryPath, new[] { "fetch", remote }, TimeSpan.FromSeconds(90), cancellationToken);

    /// <summary>
    /// Metade local do pull em lote: fast-forward da branch até o upstream já buscado. Só mexe
    /// na branch e no índice do próprio worktree, então roda em paralelo com os demais.
    /// </summary>
    public static Task<ProcessResult> MergeUpstreamFastForwardAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
        => RunAsync(worktreePath, new[] { "merge", "--ff-only", "@{upstream}" }, TimeSpan.FromSeconds(60), cancellationToken);

    /// <summary>
    /// Remoto onde mora a base do PR: o do upstream da branch, caindo para origin. A base
    /// é comparada como &lt;remoto&gt;/&lt;base&gt;, que é o que o GitHub enxerga — não a cópia local.
    /// </summary>
    public static async Task<string> ResolveRemoteAsync(
        string worktreePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            worktreePath,
            new[] { "config", "--get", $"branch.{branch}.remote" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var remote = result.Success ? result.StandardOutput.Trim() : string.Empty;

        // "." é upstream local (branch rastreando outra branch local), sem remoto de verdade.
        return remote.Length > 0 && remote != "." ? remote : "origin";
    }

    /// <summary>Atualiza só a ref remota da base — sem isso, &lt;remoto&gt;/&lt;base&gt; pode estar velho.</summary>
    public static Task<ProcessResult> FetchBranchAsync(
        string worktreePath,
        string remote,
        string branch,
        CancellationToken cancellationToken = default)
        => RunAsync(
            worktreePath,
            new[] { "fetch", remote, $"+refs/heads/{branch}:refs/remotes/{remote}/{branch}" },
            TimeSpan.FromSeconds(90),
            cancellationToken);

    /// <summary>
    /// Distância entre o HEAD do worktree e <paramref name="baseRef"/>: quantos commits a base
    /// tem que a branch não tem (Behind) e o contrário (Ahead). Null se a ref não existe.
    /// </summary>
    public static async Task<(int Behind, int Ahead)?> CountDistanceAsync(
        string worktreePath,
        string baseRef,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            worktreePath,
            new[] { "rev-list", "--left-right", "--count", $"{baseRef}...HEAD" },
            TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success) return null;

        var parts = result.StandardOutput.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var behind) && int.TryParse(parts[1], out var ahead)
            ? (behind, ahead)
            : null;
    }

    /// <summary>
    /// Há alteração em arquivo versionado? É o que faz merge e rebase recusarem. Arquivo
    /// não versionado fica de fora: não atrapalha nenhum dos dois.
    /// </summary>
    public static async Task<bool> HasTrackedChangesAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            worktreePath,
            new[] { "status", "--porcelain", "--untracked-files=no" },
            TimeSpan.FromSeconds(25),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success) throw new InvalidOperationException(result.FirstErrorLine);

        return result.StandardOutput.Trim().Length > 0;
    }

    /// <summary>
    /// Traz <paramref name="baseRef"/> para dentro da branch do worktree. Merge preserva o
    /// histórico (--no-edit: a mensagem padrão, sem abrir editor); rebase reescreve os commits
    /// da branch por cima da base. Em conflito o git para e deixa o worktree no meio da operação.
    /// </summary>
    public static Task<ProcessResult> IntegrateBaseAsync(
        string worktreePath,
        string baseRef,
        BaseUpdateStrategy strategy,
        CancellationToken cancellationToken = default)
        => RunAsync(
            worktreePath,
            strategy == BaseUpdateStrategy.Rebase
                ? new[] { "rebase", baseRef }
                : new[] { "merge", "--no-edit", baseRef },
            TimeSpan.FromMinutes(3),
            cancellationToken);

    /// <summary>Trava um worktree com `git worktree lock`, gravando o motivo se houver um.</summary>
    public static async Task<ProcessResult> LockWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        var arguments = new List<string> { "-C", repositoryPath, "worktree", "lock" };
        if (!string.IsNullOrWhiteSpace(reason)) arguments.AddRange(new[] { "--reason", reason.Trim() });
        arguments.Add(worktreePath);

        return await ProcessRunner.RunAsync(
            git,
            arguments,
            repositoryPath,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Destrava um worktree — o `remove` recusa enquanto houver lock.</summary>
    public static async Task<ProcessResult> UnlockWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        return await ProcessRunner.RunAsync(
            git,
            new[] { "-C", repositoryPath, "worktree", "unlock", worktreePath },
            repositoryPath,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

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
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Arquivos e pastas ignorados pelo .gitignore dentro do worktree — o que o
    /// `git worktree remove` apaga junto sem reclamar. Com --ignored=matching e -uall, cada
    /// pasta ignorada vem pelo próprio nome (src/bin/), não engolida por um pai não rastreado.
    /// Null se o git falhou: quem chama não pode tratar "não sei" como "não há".
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ListIgnoredAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            worktreePath,
            new[] { "status", "--porcelain", "--ignored=matching", "--untracked-files=all", "-z" },
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success) return null;

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.StartsWith("!! ", StringComparison.Ordinal))
            .Select(entry => entry[3..])
            .ToList();
    }

    /// <summary>
    /// Fetch periódico do monitoramento: todos os remotos, com --prune para que a branch
    /// apagada no remoto apareça como upstream "gone". Sem tocar no FETCH_HEAD de quem
    /// estiver usando o repositório, sem gc automático no meio e sem prompt de credencial —
    /// um app sem terminal ficaria esperando uma senha que ninguém vai digitar.
    /// </summary>
    public static async Task<ProcessResult> FetchAllInBackgroundAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var git = ExecutableLocator.Find("git") ?? throw new GitNotFoundException();

        return await ProcessRunner.RunAsync(
            git,
            new[]
            {
                "-C", repositoryPath, "-c", "gc.auto=0", "-c", "maintenance.auto=false",
                "fetch", "--all", "--prune", "--quiet", "--no-write-fetch-head",
            },
            repositoryPath,
            TimeSpan.FromSeconds(120),
            new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" },
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
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
                cancellationToken: cancellationToken).ConfigureAwait(false);

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
                    lockReason = value.Length == 0 ? null : UnquoteC(value);
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

    /// <summary>
    /// Desfaz as aspas que o git põe em campos do --porcelain com caractere especial: escapes
    /// de C (\" \\ \n \t…) e bytes fora do ASCII em octal (\303\243 = "ã"), a remontar em UTF-8.
    /// Sem aspas, o valor já veio cru.
    /// </summary>
    internal static string UnquoteC(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;

        var bytes = new List<byte>(value.Length);
        for (var i = 1; i < value.Length - 1; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length - 1)
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(c.ToString()));
                continue;
            }

            var next = value[++i];
            if (next is >= '0' and <= '7' && i + 2 < value.Length - 1)
            {
                bytes.Add(Convert.ToByte(value.Substring(i, 3), 8));
                i += 2;
                continue;
            }

            bytes.Add(next switch
            {
                'a' => (byte)'\a',
                'b' => (byte)'\b',
                'f' => (byte)'\f',
                'n' => (byte)'\n',
                'r' => (byte)'\r',
                't' => (byte)'\t',
                'v' => (byte)'\v',
                _ => (byte)next,
            });
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
}
