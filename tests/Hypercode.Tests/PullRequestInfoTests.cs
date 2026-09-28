using Hypercode.Services;

namespace Hypercode.Tests;

public class PullRequestInfoTests
{
    private static PullRequestInfo WithChecks(params CheckEntry[] checks) =>
        new() { State = "OPEN", StatusCheckRollup = checks.ToList() };

    [Fact]
    public void Checks_SemRollup_None()
    {
        Assert.Equal(ChecksState.None, new PullRequestInfo().Checks);
        Assert.Equal(ChecksState.None, WithChecks().Checks);
    }

    [Fact]
    public void Checks_TodosConcluidosComSucesso_Passing()
    {
        var pullRequest = WithChecks(
            new CheckEntry { Name = "build", Status = "COMPLETED", Conclusion = "SUCCESS" },
            new CheckEntry { Context = "legado", State = "SUCCESS" });

        Assert.Equal(ChecksState.Passing, pullRequest.Checks);
    }

    [Theory]
    [InlineData("FAILURE", null)]
    [InlineData("TIMED_OUT", null)]
    [InlineData("CANCELLED", null)]
    [InlineData("ACTION_REQUIRED", null)]
    [InlineData("STARTUP_FAILURE", null)]
    [InlineData(null, "FAILURE")]
    [InlineData(null, "ERROR")]
    public void Checks_UmFalhando_FailingMesmoComOutrosRodando(string? conclusion, string? state)
    {
        var pullRequest = WithChecks(
            new CheckEntry { Name = "lento", Status = "IN_PROGRESS" },
            new CheckEntry { Name = "quebrado", Status = "COMPLETED", Conclusion = conclusion, State = state });

        Assert.Equal(ChecksState.Failing, pullRequest.Checks);
        Assert.Equal("quebrado", pullRequest.FailingChecksSummary);
    }

    [Theory]
    [InlineData("IN_PROGRESS", null, null)]
    [InlineData("QUEUED", null, null)]
    [InlineData(null, null, "PENDING")]
    [InlineData(null, null, "EXPECTED")]
    [InlineData(null, null, null)]
    public void Checks_AlgumAindaRodando_Pending(string? status, string? conclusion, string? state)
    {
        var pullRequest = WithChecks(
            new CheckEntry { Name = "ok", Status = "COMPLETED", Conclusion = "SUCCESS" },
            new CheckEntry { Name = "rodando", Status = status, Conclusion = conclusion, State = state });

        Assert.Equal(ChecksState.Pending, pullRequest.Checks);
    }

    [Fact]
    public void FailingChecksSummary_LimitaACincoNomes()
    {
        var checks = Enumerable.Range(1, 7)
            .Select(index => new CheckEntry { Name = $"c{index}", Conclusion = "failure" })
            .ToArray();

        Assert.Equal("c1, c2, c3, c4, c5", WithChecks(checks).FailingChecksSummary);
    }

    [Theory]
    [InlineData("APPROVED", ReviewState.Approved)]
    [InlineData("changes_requested", ReviewState.ChangesRequested)]
    [InlineData("REVIEW_REQUIRED", ReviewState.Required)]
    [InlineData("", ReviewState.None)]
    [InlineData(null, ReviewState.None)]
    public void Review_MapeiaReviewDecision(string? decision, ReviewState expected)
    {
        Assert.Equal(expected, new PullRequestInfo { ReviewDecision = decision }.Review);
    }

    [Theory]
    [InlineData("OPEN", 3)]
    [InlineData("open", 3)]
    [InlineData("MERGED", 2)]
    [InlineData("CLOSED", 1)]
    [InlineData("", 1)]
    public void Relevance_AbertoAntesDeMergedAntesDeFechado(string state, int expected)
    {
        Assert.Equal(expected, new PullRequestInfo { State = state }.Relevance);
    }

    [Fact]
    public void ConflitoEBehind_SoValemComPrAberto()
    {
        var open = new PullRequestInfo { State = "OPEN", Mergeable = "CONFLICTING", MergeStateStatus = "BEHIND" };
        var merged = new PullRequestInfo { State = "MERGED", Mergeable = "CONFLICTING", MergeStateStatus = "BEHIND" };

        Assert.True(open.HasConflicts);
        Assert.True(open.IsBehindBase);
        Assert.False(merged.HasConflicts);
        Assert.False(merged.IsBehindBase);
    }

    [Fact]
    public void CheckEntry_Label_CaiParaContextoEDepoisParaCheck()
    {
        Assert.Equal("nome", new CheckEntry { Name = "nome", Context = "ctx" }.Label);
        Assert.Equal("ctx", new CheckEntry { Context = "ctx" }.Label);
        Assert.Equal("check", new CheckEntry().Label);
    }
}
