using System.Diagnostics;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Rebase parado por conflito destaca o HEAD: a linha continua identificada pela branch que
/// está sendo rebaseada (#59).
/// </summary>
public sealed class PausedRebaseTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Theory]
    [InlineData("--merge")]
    [InlineData("--apply")]
    public async Task RebasePausadoGuardaABranchDeOrigem(string backend)
    {
        var repository = _sandbox.CreateRepository("api", "feature");
        var worktree = Path.Combine(_sandbox.Root, "api-feature");
        PauseRebaseOnConflict(repository, worktree, backend);

        var info = (await GitService.ListWorktreesAsync(repository)).Single(item => item.FullPath == worktree);

        Assert.True(info.IsDetached);
        Assert.Null(info.Branch);
        Assert.Equal("feature", info.RebasingBranch);
        Assert.Equal("feature", info.TrackedBranch);

        var row = new WorktreeRow(info)
        {
            PullRequest = new PullRequestInfo { Number = 965, State = "MERGED", HeadRefName = "feature" },
        };
        Assert.Equal("feature (rebase em andamento)", row.Branch);
        Assert.False(row.IsCompleted);
        Assert.True(row.IsKnownNotCompleted);

        var lookup = new PullRequestLookup(
            new Dictionary<string, PullRequestInfo> { ["feature"] = row.PullRequest! }, null);
        Assert.Same(row.PullRequest, RepositoryViewModel.FindPullRequest(info, lookup, new HashSet<string> { "main", "feature" }));
    }

    [Fact]
    public async Task RebaseDeHeadDestacadoContinuaSemBranch()
    {
        var repository = _sandbox.CreateRepository("api", "feature");
        var worktree = Path.Combine(_sandbox.Root, "api-feature");
        Git(worktree, "checkout", "--quiet", "--detach");
        PauseRebaseOnConflict(repository, worktree, "--merge");

        var info = (await GitService.ListWorktreesAsync(repository)).Single(item => item.FullPath == worktree);

        Assert.Null(info.RebasingBranch);
        Assert.StartsWith("detached @ ", new WorktreeRow(info).Branch);
    }

    [Fact]
    public async Task SemRebaseNadaMuda()
    {
        var repository = _sandbox.CreateRepository("api", "feature");

        var worktrees = await GitService.ListWorktreesAsync(repository);

        Assert.All(worktrees, info => Assert.Null(info.RebasingBranch));
        Assert.Equal(new[] { "main", "feature" }, worktrees.Select(info => info.TrackedBranch));
    }

    /// <summary>Mesmo arquivo mudado na main e no worktree, e o rebase do worktree sobre a main.</summary>
    private static void PauseRebaseOnConflict(string repository, string worktree, string backend)
    {
        Commit(repository, "main");
        Commit(worktree, "feature");

        var start = new ProcessStartInfo("git") { WorkingDirectory = worktree, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-c", "user.name=Teste", "-c", "user.email=teste@exemplo.com", "rebase", backend, "main" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.NotEqual(0, process.ExitCode);
    }

    private static void Commit(string directory, string content)
    {
        File.WriteAllText(Path.Combine(directory, "arquivo.txt"), content + "\n");
        Git(directory, "add", "arquivo.txt");
        Git(directory, "-c", "user.name=Teste", "-c", "user.email=teste@exemplo.com", "commit", "--quiet", "-m", content);
    }

    private static void Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
    }
}
