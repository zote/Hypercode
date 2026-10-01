using Hypercode.Services;

namespace Hypercode.Tests;

public class GitServiceTests
{
    [Fact]
    public void ParsePorcelain_PrimeiroBlocoEOPrincipal()
    {
        var worktrees = GitService.ParsePorcelain(
            """
            worktree /repo
            HEAD 1111111111111111111111111111111111111111
            branch refs/heads/main

            worktree /repo.worktrees/feature-login
            HEAD 2222222222222222222222222222222222222222
            branch refs/heads/feature/login
            locked

            worktree /repo.worktrees/solto
            HEAD 3333333333333333333333333333333333333333
            detached
            prunable gitdir file points to non-existent location

            """);

        Assert.Equal(3, worktrees.Count);

        Assert.True(worktrees[0].IsMain);
        Assert.Equal("main", worktrees[0].Branch);
        Assert.Equal("repo", worktrees[0].Name);

        Assert.False(worktrees[1].IsMain);
        Assert.Equal("feature/login", worktrees[1].Branch);
        Assert.Equal("feature-login", worktrees[1].Name);
        Assert.True(worktrees[1].IsLocked);
        Assert.Null(worktrees[1].LockReason);

        Assert.True(worktrees[2].IsDetached);
        Assert.True(worktrees[2].IsPrunable);
        Assert.Null(worktrees[2].Branch);
        Assert.Equal("3333333", worktrees[2].ShortHead);
    }

    [Fact]
    public void ParsePorcelain_Bare()
    {
        var worktrees = GitService.ParsePorcelain("worktree /srv/repo.git\nbare\n");

        Assert.Single(worktrees);
        Assert.True(worktrees[0].IsBare);
        Assert.True(worktrees[0].IsMain);
    }

    [Fact]
    public void ParsePorcelain_SemLinhaEmBrancoNoFim_NaoPerdeOUltimo()
    {
        var worktrees = GitService.ParsePorcelain("worktree /a\nbranch refs/heads/a\n\nworktree /b\nbranch refs/heads/b");

        Assert.Equal(["a", "b"], worktrees.Select(worktree => worktree.Branch));
    }

    [Theory]
    [InlineData("{\"owner\":\"supacode\",\"id\":1}", "supacode")]
    [InlineData("\"{\\\"owner\\\":\\\"supacode\\\"}\"", "supacode")]
    [InlineData("{\"owner\": \"x\", quebrado", "x")]
    [InlineData("não mexa, estou usando", null)]
    [InlineData("{\"owner\":\"\"}", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractLockOwner_SoReconheceJsonComOwner(string? reason, string? expected)
    {
        Assert.Equal(expected, WorktreeInfo.ExtractLockOwner(reason));
    }

    [Fact]
    public void LockDeFerramenta_NaoELockManual()
    {
        var tool = new WorktreeInfo { FullPath = "/a", IsLocked = true, LockReason = "{\"owner\":\"supacode\"}" };
        var manual = new WorktreeInfo { FullPath = "/b", IsLocked = true, LockReason = "trabalho em andamento" };

        Assert.True(tool.IsToolLock);
        Assert.Equal("travado por supacode", tool.LockDescription);
        Assert.False(manual.IsToolLock);
        Assert.Equal("trabalho em andamento", manual.LockDescription);
    }
}
