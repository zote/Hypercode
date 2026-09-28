using System.Globalization;
using System.Text;

namespace Hypercode.Services;

public sealed record WorktreeCreationResult(
    string Path,
    string Branch,
    int CopiedFiles,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Cria worktrees em &lt;pai do repo&gt;/&lt;repo&gt;.worktrees/&lt;branch&gt; — uma branch nova a partir
/// de uma base (também a de uma issue) ou a branch de um PR — e copia do principal os arquivos não versionados
/// listados no .worktreeinclude.
/// </summary>
public static class WorktreeCreator
{
    public const string IncludeFileName = ".worktreeinclude";

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Pasta que agrupa os worktrees: /dev/meu-repo → /dev/meu-repo.worktrees</summary>
    public static string WorktreesRoot(string mainWorktreePath)
    {
        var trimmed = mainWorktreePath.TrimEnd('/');
        var parent = Path.GetDirectoryName(trimmed) ?? trimmed;
        return Path.Combine(parent, Path.GetFileName(trimmed) + ".worktrees");
    }

    /// <summary>
    /// Pasta sugerida para a branch. A barra vira hífen (feature/login → feature-login)
    /// para cada worktree ser uma pasta só, e o nome na lista não virar só "login".
    /// </summary>
    public static string SuggestPath(string mainWorktreePath, string branch)
    {
        var folder = string.Join('-', branch.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return folder.Length == 0 ? string.Empty : Path.Combine(WorktreesRoot(mainWorktreePath), folder);
    }

    /// <summary>
    /// Branch de trabalho de uma issue: claude/issue-&lt;numero&gt;-&lt;slug&gt;, o padrão do AGENTS.md.
    /// O slug são as primeiras palavras do título que carregam sentido, sem acento nem pontuação:
    /// "Criar worktree a partir de uma issue" → claude/issue-34-criar-worktree-partir-issue.
    /// </summary>
    public static string SuggestIssueBranch(int number, string title)
    {
        var slug = string.Join('-', SlugWords(title).Take(IssueSlugWords));
        return slug.Length == 0 ? $"claude/issue-{number}" : $"claude/issue-{number}-{slug}";
    }

    private const int IssueSlugWords = 4;

    private static readonly HashSet<string> SlugStopWords = new(StringComparer.Ordinal)
    {
        "a", "o", "as", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas",
        "um", "uma", "para", "pra", "por", "pelo", "pela", "com", "sem", "que", "se", "ao", "the", "of", "to", "and",
    };

    private static IEnumerable<string> SlugWords(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return plain.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !SlugStopWords.Contains(word));
    }

    /// <summary>origin quando existe; senão o primeiro remoto; null sem remoto.</summary>
    public static async Task<string?> PreferredRemoteAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var remotes = await GitService.ListRemotesAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        return remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
    }

    /// <summary>Padrões do .worktreeinclude (sem comentários e linhas vazias), só para exibir.</summary>
    public static IReadOnlyList<string> ReadIncludePatterns(string mainWorktreePath)
    {
        var file = Path.Combine(mainWorktreePath, IncludeFileName);
        if (!File.Exists(file)) return Array.Empty<string>();

        try
        {
            return File.ReadAllLines(file)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// git worktree add -b &lt;branch&gt; &lt;path&gt; &lt;base&gt;, com fetch da base antes quando ela é
    /// remota. --no-track: a branch nova não herda a base como upstream — senão o primeiro
    /// push iria para a main e o app mostraria "atrás/à frente" da main em vez de "nunca pushada".
    /// </summary>
    public static async Task<WorktreeCreationResult> CreateFromNewBranchAsync(
        string mainWorktreePath,
        string branch,
        string baseRef,
        string path,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        if (!await GitService.IsValidBranchNameAsync(mainWorktreePath, branch, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"'{branch}' não é um nome de branch válido para o git.");

        if (await GitService.RefExistsAsync(mainWorktreePath, $"refs/heads/{branch}", cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"A branch '{branch}' já existe. Escolha outro nome.");

        EnsurePathIsFree(path);

        var remotes = await GitService.ListRemotesAsync(mainWorktreePath, cancellationToken).ConfigureAwait(false);
        var remote = remotes.FirstOrDefault(name => baseRef.StartsWith(name + "/", StringComparison.Ordinal));

        if (remote is not null)
        {
            var remoteBranch = baseRef[(remote.Length + 1)..];
            var fetch = await TryFetchAsync(
                mainWorktreePath,
                new[] { "fetch", remote, $"+refs/heads/{remoteBranch}:refs/remotes/{remote}/{remoteBranch}" },
                cancellationToken).ConfigureAwait(false);

            if (fetch is not null)
                warnings.Add($"fetch de {baseRef} falhou ({fetch}) — criado a partir da cópia local.");
        }

        var verify = await GitService.RunAsync(
            mainWorktreePath,
            new[] { "rev-parse", "--verify", "--quiet", baseRef + "^{commit}" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!verify.Success)
            throw new InvalidOperationException($"A base '{baseRef}' não existe.");

        await AddWorktreeAsync(
            mainWorktreePath,
            new[] { "worktree", "add", "--no-track", "-b", branch, path, baseRef },
            cancellationToken).ConfigureAwait(false);

        var copied = await CopyIncludedFilesAsync(mainWorktreePath, path, warnings, cancellationToken).ConfigureAwait(false);
        return new WorktreeCreationResult(path, branch, copied, warnings);
    }

    /// <summary>
    /// Worktree na branch do PR, com o mesmo nome da head (headRefName):
    /// PR do próprio repo → branch local rastreando &lt;remoto&gt;/&lt;head&gt;, pronta para push;
    /// PR de fork → pull/N/head baixado direto para a branch local, sem upstream;
    /// branch local já existente → usada como está.
    /// </summary>
    public static async Task<WorktreeCreationResult> CreateFromPullRequestAsync(
        string mainWorktreePath,
        PullRequestHead pullRequest,
        string path,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var branch = pullRequest.HeadRefName;

        if (string.IsNullOrEmpty(branch))
            throw new InvalidOperationException($"O PR #{pullRequest.Number} não informou a branch de origem.");

        EnsurePathIsFree(path);

        var remote = await PreferredRemoteAsync(mainWorktreePath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O repositório não tem remoto configurado — não há de onde baixar o PR.");

        var localExists = await GitService
            .RefExistsAsync(mainWorktreePath, $"refs/heads/{branch}", cancellationToken)
            .ConfigureAwait(false);

        if (localExists)
        {
            if (pullRequest.IsCrossRepository)
                throw new InvalidOperationException(
                    $"O PR #{pullRequest.Number} vem de um fork, e já existe uma branch local '{branch}' "
                    + "que pode não ser a dele. Renomeie ou apague a branch local e tente de novo.");

            warnings.Add($"a branch local '{branch}' já existia — usada como está, sem pull.");

            await AddWorktreeAsync(
                mainWorktreePath,
                new[] { "worktree", "add", path, branch },
                cancellationToken).ConfigureAwait(false);
        }
        else if (!pullRequest.IsCrossRepository)
        {
            var fetchError = await TryFetchAsync(
                mainWorktreePath,
                new[] { "fetch", remote, $"+refs/heads/{branch}:refs/remotes/{remote}/{branch}" },
                cancellationToken).ConfigureAwait(false);

            if (fetchError is not null)
                throw new InvalidOperationException($"Não consegui baixar {remote}/{branch}: {fetchError}");

            await AddWorktreeAsync(
                mainWorktreePath,
                new[] { "worktree", "add", "--track", "-b", branch, path, $"{remote}/{branch}" },
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var fetchError = await TryFetchAsync(
                mainWorktreePath,
                new[] { "fetch", remote, $"pull/{pullRequest.Number}/head:refs/heads/{branch}" },
                cancellationToken).ConfigureAwait(false);

            if (fetchError is not null)
                throw new InvalidOperationException($"Não consegui baixar o PR #{pullRequest.Number}: {fetchError}");

            warnings.Add("PR de fork: a branch local não tem upstream — push exige o remoto do fork.");

            await AddWorktreeAsync(
                mainWorktreePath,
                new[] { "worktree", "add", path, branch },
                cancellationToken).ConfigureAwait(false);
        }

        var copied = await CopyIncludedFilesAsync(mainWorktreePath, path, warnings, cancellationToken).ConfigureAwait(false);
        return new WorktreeCreationResult(path, branch, copied, warnings);
    }

    private static void EnsurePathIsFree(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Informe a pasta do worktree.");

        if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            throw new InvalidOperationException($"'{path}' já existe e não está vazia.");
    }

    private static async Task AddWorktreeAsync(
        string mainWorktreePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        // O checkout de um repo grande pode demorar bem mais que os 30 s padrão.
        var result = await GitService
            .RunAsync(mainWorktreePath, arguments, TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException($"git worktree add falhou: {LastMeaningfulLine(result)}");
    }

    /// <summary>Devolve null em caso de sucesso, ou a mensagem de erro.</summary>
    private static async Task<string?> TryFetchAsync(
        string mainWorktreePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await GitService
                .RunAsync(mainWorktreePath, arguments, FetchTimeout, cancellationToken)
                .ConfigureAwait(false);

            return result.Success ? null : LastMeaningfulLine(result);
        }
        catch (TimeoutException exception)
        {
            return exception.Message;
        }
    }

    // O git escreve progresso no stderr ("Preparing worktree…"); a causa do erro é a última linha.
    private static string LastMeaningfulLine(ProcessResult result)
        => result.StandardError
               .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .LastOrDefault()
           ?? result.FirstErrorLine;

    /// <summary>
    /// Copia do principal os arquivos que casam com o .worktreeinclude E estão ignorados pelo
    /// git (a mesma regra do Claude Code): arquivo versionado já vem no checkout, e um padrão
    /// largo demais não arrasta nada que o git enxergue. Nunca sobrescreve no destino.
    /// </summary>
    private static async Task<int> CopyIncludedFilesAsync(
        string mainWorktreePath,
        string destination,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var includeFile = Path.Combine(mainWorktreePath, IncludeFileName);
        if (!File.Exists(includeFile)) return 0;

        try
        {
            // --directory recolhe pastas inteiras numa entrada "pasta/", o que mantém as listas
            // curtas mesmo com node_modules/bin/obj ignorados.
            var included = await ListOthersAsync(
                mainWorktreePath, $"--exclude-from={includeFile}", cancellationToken).ConfigureAwait(false);

            if (included.Count == 0) return 0;

            var ignored = (await ListOthersAsync(
                mainWorktreePath, "--exclude-standard", cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

            bool IsIgnored(string relative)
            {
                if (ignored.Contains(relative)) return true;
                for (var slash = relative.IndexOf('/'); slash >= 0; slash = relative.IndexOf('/', slash + 1))
                    if (ignored.Contains(relative[..(slash + 1)])) return true;
                return false;
            }

            return await Task.Run(() =>
            {
                var copied = 0;

                foreach (var entry in included)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var files = entry.EndsWith('/')
                        ? EnumerateFilesSafe(Path.Combine(mainWorktreePath, entry))
                            .Select(file => Path.GetRelativePath(mainWorktreePath, file).Replace('\\', '/'))
                        : new[] { entry };

                    foreach (var relative in files.Where(IsIgnored))
                    {
                        var target = Path.Combine(destination, relative);
                        if (File.Exists(target)) continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(Path.Combine(mainWorktreePath, relative), target);
                        copied++;
                    }
                }

                return copied;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // O worktree já existe; falhar na cópia não pode desfazer isso.
            warnings.Add($"cópia do {IncludeFileName} falhou: {exception.Message}");
            return 0;
        }
    }

    private static async Task<IReadOnlyList<string>> ListOthersAsync(
        string mainWorktreePath,
        string excludeOption,
        CancellationToken cancellationToken)
    {
        var result = await GitService.RunAsync(
            mainWorktreePath,
            new[] { "ls-files", "--others", "--ignored", "--directory", excludeOption, "-z" },
            TimeSpan.FromSeconds(60),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(result.FirstErrorLine);

        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            })
            : Enumerable.Empty<string>();
}
