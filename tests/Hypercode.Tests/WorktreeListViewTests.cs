using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Tests;

public class WorktreeListViewTests
{
    private static WorktreeRow Row(string name, string? branch, int? pullRequest = null, string title = "", bool main = false)
        => new(new WorktreeInfo { FullPath = $"/nao-existe/{name}", Branch = branch, IsMain = main })
        {
            PullRequest = pullRequest is { } number ? new PullRequestInfo { Number = number, Title = title, State = "OPEN" } : null,
        };

    private static readonly WorktreeRow Main = Row("repo", "main", main: true);
    private static readonly WorktreeRow Login = Row("login", "feature/login", 412, "Tela de login");
    private static readonly WorktreeRow Hotfix = Row("hotfix", "hotfix/crash", 418, "Corrige a exceção ao abrir");
    private static readonly WorktreeRow Spike = Row("alpha-spike", "spike/alpha");

    private static readonly WorktreeRow[] All = [Hotfix, Spike, Main, Login];

    private static List<string> Names(IEnumerable<WorktreeRow> rows) => rows.Select(row => row.Name).ToList();

    [Fact]
    public void SemFiltro_PorNome_PrincipalNoTopo()
    {
        var result = WorktreeListView.Arrange(All, "", SortColumn.Name, descending: false);

        Assert.Equal(["repo", "alpha-spike", "hotfix", "login"], Names(result));
    }

    [Fact]
    public void PorNomeDecrescente_PrincipalContinuaNoTopo()
    {
        var result = WorktreeListView.Arrange(All, "", SortColumn.Name, descending: true);

        Assert.Equal(["repo", "login", "hotfix", "alpha-spike"], Names(result));
    }

    [Theory]
    [InlineData(false, new[] { "repo", "login", "hotfix", "alpha-spike" })]
    [InlineData(true, new[] { "repo", "hotfix", "login", "alpha-spike" })]
    public void PorPr_SemPrVaiProFimNosDoisSentidos(bool descending, string[] expected)
    {
        var result = WorktreeListView.Arrange(All, "", SortColumn.PullRequest, descending);

        Assert.Equal(expected, Names(result));
    }

    [Fact]
    public void PorBranch()
    {
        var result = WorktreeListView.Arrange(All, "", SortColumn.Branch, descending: false);

        Assert.Equal(["main", "feature/login", "hotfix/crash", "spike/alpha"], result.Select(row => row.Branch));
    }

    [Theory]
    [InlineData("login 412", new[] { "login" })]
    [InlineData("#41", new[] { "login", "hotfix" })]
    [InlineData("EXCECAO", new[] { "hotfix" })]
    [InlineData("  spike  ", new[] { "alpha-spike" })]
    [InlineData("login 418", new string[0])]
    public void Filtro_TodaPalavraPrecisaCasar(string filter, string[] expected)
    {
        var result = WorktreeListView.Arrange(All, filter, SortColumn.PullRequest, descending: false);

        Assert.Equal(expected, Names(result));
    }

    [Fact]
    public void Filtro_NumeroSoCasaPeloComeco()
    {
        Assert.True(Login.Matches("41"));
        Assert.False(Login.Matches("12"));
        Assert.False(Spike.Matches("#"));
    }
}
