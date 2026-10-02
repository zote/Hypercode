using System.Text.Json;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Qual worktree já trata cada issue do grafo (#148): pelo PR que declara fechá-la e, sem
/// declaração, pelo número no nome da branch. Sem git nem GitHub.
/// </summary>
public sealed class IssueWorktreeLinkTests
{
    private const string Repo = "Zimps/locanota";

    // ── Número pela branch ──────────────────────────────────────────────────

    [Theory]
    [InlineData("claude/issue-1269-colunas-art-487-importacao", 1269)]
    [InlineData("claude/issue-1275-preparar-fundacao-computacao-assinatura", 1275)]
    [InlineData("feat/880-simulador-estrutura", 880)]
    [InlineData("feat/899-versao-da-regra", 899)]
    [InlineData("codex/966-fila-20260925", 966)]
    [InlineData("issue-12-sem-prefixo", 12)]
    [InlineData("feat/42", 42)]
    [InlineData("Claude/Issue-7-caixa", 7)]
    public void NumeroDaBranchNasTresConvencoes(string branch, int number)
        => Assert.Equal(number, IssueBranch.Number(branch));

    [Theory]
    [InlineData("main")]
    [InlineData("feat/simulador-estrutura")]
    [InlineData("release/2026.10")]
    [InlineData("fix/v2-login")]
    [InlineData("tissue-12-nao-e-issue")]
    [InlineData("")]
    [InlineData(null)]
    public void BranchSemNumeroNaoLiga(string? branch)
        => Assert.Null(IssueBranch.Number(branch));

    [Fact]
    public void IssueVemAntesDaBarra()
        // "issue-" vence o número logo depois da barra: a convenção do AGENTS.md é a explícita.
        => Assert.Equal(55, IssueBranch.Number("claude/issue-55-de-2026"));

    // ── Ligação ─────────────────────────────────────────────────────────────

    private static WorktreeRow Row(string name, string branch, PullRequestInfo? pullRequest = null)
        => new(new WorktreeInfo { FullPath = $"/dev/{name}", Branch = branch }) { PullRequest = pullRequest };

    private static PullRequestInfo Pr(int number, string state = "OPEN", params string[] closes)
        => new()
        {
            Number = number,
            State = state,
            Url = $"https://github.com/{Repo}/pull/{number}",
            ClosingIssuesReferences = closes
                .Select(issue => issue.Split('#') is [var repository, var n]
                    ? new ClosingIssueReference { Number = int.Parse(n), Url = $"https://github.com/{repository}/issues/{n}" }
                    : new ClosingIssueReference { Number = int.Parse(issue), Url = $"https://github.com/{Repo}/issues/{issue}" })
                .ToList(),
        };

    private static IssueKey K(int number) => new(Repo, number);

    [Fact]
    public void PrDeclaradoLigaComOrigemNoTooltip()
    {
        var row = Row("fundacao", "claude/issue-1275-preparar", Pr(1295, closes: "1275"));

        var link = IssueWorktreeLink.Build(Repo, new[] { row })[K(1275)];

        Assert.Same(row, link.Row);
        Assert.Equal(IssueLinkSource.PullRequest, link.Source);
        Assert.Contains("pelo PR #1295", link.Tooltip);
    }

    [Fact]
    public void PrSemPalavraChaveCaiParaABranch()
    {
        // O caso do PR 914: feat/899-…, corpo sem "Closes".
        var row = Row("versao", "feat/899-versao-da-regra", Pr(914));

        var link = IssueWorktreeLink.Build(Repo, new[] { row })[K(899)];

        Assert.Equal(IssueLinkSource.Branch, link.Source);
        Assert.Contains("pelo nome da branch", link.Tooltip);
    }

    [Fact]
    public void WorktreeSemPrLigaPelaBranch()
    {
        var links = IssueWorktreeLink.Build(Repo, new[] { Row("fila", "codex/966-fila-20260925") });

        Assert.Equal(IssueLinkSource.Branch, links[K(966)].Source);
    }

    [Fact]
    public void DivergenciaVenceADeclarada()
    {
        // A branch diz 880, o PR declara que fecha a 881: só a 881 fica ligada.
        var links = IssueWorktreeLink.Build(Repo, new[] { Row("sim", "feat/880-simulador", Pr(895, closes: "881")) });

        Assert.Equal(new[] { K(881) }, links.Keys);
    }

    [Fact]
    public void IssueDeOutroRepositorioNuncaLiga()
    {
        // O PR fecha a issue 12 de outro repositório: nem ela, nem a 12 daqui pela branch.
        var row = Row("cross", "claude/issue-12-cross", Pr(30, closes: "Zimps/outro#12"));

        Assert.Empty(IssueWorktreeLink.Build(Repo, new[] { row }));
    }

    [Fact]
    public void RepositorioSemDiferencaDeCaixa()
    {
        var row = Row("caixa", "x", Pr(3, closes: "zimps/LOCANOTA#4"));

        Assert.True(IssueWorktreeLink.Build(Repo, new[] { row }).ContainsKey(K(4)));
    }

    [Fact]
    public void DuasBranchesParaAMesmaIssueAvisamEFicaADeclarada()
    {
        var palpite = Row("antigo", "feat/50-primeira-tentativa");
        var declarada = Row("novo", "claude/issue-51-outra", Pr(60, closes: "50"));

        var link = IssueWorktreeLink.Build(Repo, new[] { palpite, declarada })[K(50)];

        Assert.Same(declarada, link.Row);
        Assert.Equal(new[] { palpite }, link.Others);
        Assert.Contains("Há mais 1 worktree para ela: antigo", link.Tooltip);
    }

    [Fact]
    public void EntreDuasPelaBranchFicaODePrAbertoDepoisOMaisNovo()
    {
        var fechado = Row("a", "feat/70-a", Pr(100, "CLOSED"));
        var aberto = Row("b", "feat/70-b", Pr(90));
        var semPr = Row("c", "feat/70-c");

        Assert.Same(aberto, IssueWorktreeLink.Build(Repo, new[] { fechado, semPr, aberto })[K(70)].Row);
    }

    [Fact]
    public void PrQueFechaDuasLigaAsDuas()
    {
        var links = IssueWorktreeLink.Build(Repo, new[] { Row("par", "feat/x", Pr(5, closes: new[] { "1", "2" })) });

        Assert.Equal(new[] { 1, 2 }, links.Keys.Select(key => key.Number).OrderBy(n => n));
    }

    [Fact]
    public void BareNaoLiga()
    {
        var bare = new WorktreeRow(new WorktreeInfo { FullPath = "/dev/repo.git", Branch = "feat/9-x", IsBare = true });

        Assert.Empty(IssueWorktreeLink.Build(Repo, new[] { bare }));
    }

    // ── Leitura do GitHub ───────────────────────────────────────────────────

    [Fact]
    public void GraphQlTrazAsIssuesFechadasPeloAlias()
    {
        using var document = JsonDocument.Parse("""
            { "number": 1295, "headRefName": "claude/issue-1275-x", "state": "OPEN", "url": "https://github.com/Zimps/locanota/pull/1295",
              "closingIssues": { "nodes": [ { "number": 1275, "url": "https://github.com/Zimps/locanota/issues/1275" } ] } }
            """);

        var pullRequest = GitHubService.ParseGraphNode(document.RootElement)!;

        Assert.Equal(new[] { K(1275) }, pullRequest.ClosingIssues);
    }

    [Fact]
    public void GraphQlSemOCampoDeixaNulo()
    {
        using var document = JsonDocument.Parse("""{ "number": 1, "headRefName": "x", "state": "OPEN" }""");

        Assert.Null(GitHubService.ParseGraphNode(document.RootElement)!.ClosingIssues);
    }

    [Fact]
    public void PrListTrazAsIssuesFechadasComoLista()
    {
        var pullRequests = JsonSerializer.Deserialize<List<PullRequestInfo>>("""
            [ { "number": 149, "headRefName": "claude/issue-146-x", "state": "OPEN",
                "closingIssuesReferences": [ { "id": "I_1", "number": 146, "repository": { "name": "Hypercode", "owner": { "login": "zote" } },
                                               "url": "https://github.com/zote/Hypercode/issues/146" } ] } ]
            """, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(new[] { new IssueKey("zote/Hypercode", 146) }, pullRequests[0].ClosingIssues);
    }
}
