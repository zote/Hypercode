using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// O grafo de issues (#144) com grafos sintéticos: alcance transitivo, livres, ciclos e
/// camadas. Sem GitHub — a leitura é trocada por dados montados aqui.
/// </summary>
public sealed class IssueGraphTests
{
    private const string Repo = "Zimps/locanota";

    private static IssueRef Ref(int number, bool open = true, string repository = Repo)
        => new(repository, number, $"issue {number}", $"https://github.com/{repository}/issues/{number}", open);

    private static IssueData Issue(
        int number,
        int[]? blocks = null,
        int[]? blockedBy = null,
        string? milestone = null,
        string? type = null,
        string[]? labels = null)
        => new(
            Repo, number, $"issue {number}", $"https://github.com/{Repo}/issues/{number}",
            type, type is null ? null : "#0969da", milestone,
            (labels ?? Array.Empty<string>()).Select(label => new IssueLabel(label, "#a2eeef")).ToList(),
            (blockedBy ?? Array.Empty<int>()).Select(n => Ref(n)).ToList(),
            (blocks ?? Array.Empty<int>()).Select(n => Ref(n)).ToList());

    /// <summary>Cada par é (bloqueadora, bloqueada); toda issue citada entra aberta.</summary>
    private static IReadOnlyList<IssueData> Edges(params (int From, int To)[] edges)
        => edges.SelectMany(edge => new[] { edge.From, edge.To }).Distinct()
            .Select(number => Issue(number, blocks: edges.Where(edge => edge.From == number).Select(edge => edge.To).ToArray()))
            .ToList();

    private static IssueKey K(int number, string repository = Repo) => new(repository, number);

    private static int[] Numbers(IEnumerable<IssueKey> keys) => keys.Select(key => key.Number).OrderBy(n => n).ToArray();

    // ── Alcance transitivo ──────────────────────────────────────────────────

    [Fact]
    public void AlcanceSomaTodosOsNiveis()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (2, 3), (3, 4), (1, 5)));

        Assert.Equal(new[] { 2, 3, 4, 5 }, Numbers(graph.Reach(K(1))));
        Assert.Equal(new[] { 3, 4 }, Numbers(graph.Reach(K(2))));
        Assert.Empty(graph.Reach(K(4)));
    }

    [Fact]
    public void AlcanceContaUmaVezQuemChegaPorDoisCaminhos()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (1, 3), (2, 4), (3, 4)));

        Assert.Equal(3, graph.Reach(K(1)).Count);
    }

    [Fact]
    public void AsDuasPontasDaMesmaDependenciaNaoDuplicamAresta()
    {
        // A #1 diz que bloqueia a #2, e a #2 diz que é bloqueada pela #1.
        var graph = IssueGraph.Build(Repo, new[] { Issue(1, blocks: new[] { 2 }), Issue(2, blockedBy: new[] { 1 }) });

        Assert.Equal(1, graph.EdgeCount);
    }

    // ── Livres ──────────────────────────────────────────────────────────────

    [Fact]
    public void LivreEhSemBloqueadorAberto()
    {
        var graph = IssueGraph.Build(Repo, Edges((1209, 1275), (1, 1275), (2, 1275), (1275, 1278)));

        Assert.True(graph.IsFree(K(1209)));
        Assert.False(graph.IsFree(K(1275)));
        Assert.Equal(3, graph.OpenBlockers(K(1275)));
        Assert.Equal(1278, Assert.Single(graph.Reach(K(1275))).Number);
    }

    [Fact]
    public void BloqueadorFechadoNaoSeguraMais()
    {
        // O GitHub mantém a relação depois de fechar: o totalCount do blockedBy continuaria 1.
        var graph = IssueGraph.Build(Repo, new[]
        {
            new IssueData(Repo, 2, "aberta", "", null, null, null, Array.Empty<IssueLabel>(), new[] { Ref(9, open: false) }, Array.Empty<IssueRef>()),
        });

        Assert.True(graph.IsFree(K(2)));
        Assert.False(graph.Nodes[K(9)].IsOpen);
        Assert.False(graph.Nodes[K(9)].IsLocalOpen);
    }

    [Fact]
    public void BloqueadorDeOutroRepositorioViraNoDeForaESegura()
    {
        var graph = IssueGraph.Build(Repo, new[]
        {
            new IssueData(Repo, 2, "aberta", "", null, null, null, Array.Empty<IssueLabel>(), new[] { Ref(5, repository: "Zimps/infra") }, Array.Empty<IssueRef>()),
        });

        var external = graph.Nodes[K(5, "zimps/INFRA")];
        Assert.True(external.IsExternal);
        Assert.False(graph.IsFree(K(2)));
    }

    // ── Ciclos ──────────────────────────────────────────────────────────────

    [Fact]
    public void CicloViraAvisoENaoTrava()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (2, 3), (3, 1), (3, 4), (5, 6)));

        var cycle = Assert.Single(graph.Cycles());
        Assert.Equal(new[] { 1, 2, 3 }, Numbers(cycle));

        // O alcance termina e não conta a própria issue.
        Assert.Equal(new[] { 2, 3, 4 }, Numbers(graph.Reach(K(1))));
        Assert.Equal(new[] { 1, 2 }, Numbers(graph.Upstream(K(3))));
    }

    [Fact]
    public void DoisCiclosSeparados()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (2, 1), (10, 11), (11, 12), (12, 10)));

        var cycles = graph.Cycles();
        Assert.Equal(2, cycles.Count);
        Assert.Equal(new[] { 1, 2 }, Numbers(cycles[0]));
        Assert.Equal(new[] { 10, 11, 12 }, Numbers(cycles[1]));
    }

    [Fact]
    public void CicloQuePassaPorIssueFechadaNaoConta()
    {
        // A #2 fechada no meio: ela já não bloqueia, então o ciclo não prende ninguém.
        var graph = IssueGraph.Build(Repo, new[]
        {
            Issue(1) with { Blocking = new[] { Ref(2, open: false) } },
            Issue(3, blocks: new[] { 1 }) with { BlockedBy = new[] { Ref(2, open: false) } },
        });

        Assert.Equal(3, graph.EdgeCount);
        Assert.Empty(graph.Cycles());
    }

    [Fact]
    public void CadeiaLongaNaoEstouraAPilha()
    {
        // Recursão de 20 mil níveis derrubaria o app; o Tarjan e o alcance são iterativos.
        var edges = Enumerable.Range(1, 20_000).Select(n => (n, n + 1)).Append((20_001, 1)).ToArray();
        var graph = IssueGraph.Build(Repo, Edges(edges));

        Assert.Equal(20_001, Assert.Single(graph.Cycles()).Count);
        Assert.Equal(20_000, graph.Reach(K(1)).Count);
    }

    // ── Camadas ─────────────────────────────────────────────────────────────

    [Fact]
    public void CamadaEhOCaminhoMaisLongoDesdeQuemNaoEhBloqueado()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (2, 3), (1, 3), (4, 3)));
        var layout = IssueLayout.Compute(graph);

        Assert.Equal(0, layout.LayerOf(K(1)));
        Assert.Equal(0, layout.LayerOf(K(4)));
        Assert.Equal(1, layout.LayerOf(K(2)));
        Assert.Equal(2, layout.LayerOf(K(3)));
    }

    [Fact]
    public void TodaArestaForaDeCicloAndaParaADireita()
    {
        var graph = IssueGraph.Build(Repo, Edges((1, 5), (2, 5), (5, 7), (3, 6), (6, 7), (7, 8), (2, 8), (4, 6)));
        var layout = IssueLayout.Compute(graph);

        Assert.All(graph.Edges, edge => Assert.True(layout.LayerOf(edge.From) < layout.LayerOf(edge.To), edge.ToString()));
        Assert.Equal(graph.Nodes.Count, layout.Layers.Sum(layer => layer.Count(key => !IssueLayout.IsWaypoint(key))));
    }

    [Fact]
    public void ArestaQuePulaColunaPassaPorUmVaoEmCadaColunaDoMeio()
    {
        // 1→4 pula as colunas da 2 e da 3: dois pontos de passagem, um em cada.
        var graph = IssueGraph.Build(Repo, Edges((1, 2), (2, 3), (3, 4), (1, 4)));
        var layout = IssueLayout.Compute(graph);

        var route = layout.Route(K(1), K(4));
        Assert.Equal(2, route.Count);
        Assert.All(route, key => Assert.True(IssueLayout.IsWaypoint(key)));
        Assert.Equal(new[] { 1, 2 }, route.Select(layout.LayerOf));
        Assert.Empty(layout.Route(K(1), K(2)));
        Assert.False(IssueLayout.IsWaypoint(K(1)));
    }

    [Fact]
    public void MembrosDeUmCicloDividemACamada()
    {
        var graph = IssueGraph.Build(Repo, Edges((0, 1), (1, 2), (2, 1), (2, 3)));
        var layout = IssueLayout.Compute(graph);

        Assert.Equal(0, layout.LayerOf(K(0)));
        Assert.Equal(1, layout.LayerOf(K(1)));
        Assert.Equal(1, layout.LayerOf(K(2)));
        Assert.Equal(2, layout.LayerOf(K(3)));
    }

    [Fact]
    public void BaricentroDesfazCruzamento()
    {
        // Pela ordem do número, 1→4 e 2→3 se cruzam; trocando 3 e 4 de lugar, não.
        var graph = IssueGraph.Build(Repo, Edges((1, 4), (2, 3)));

        var initial = new IReadOnlyList<IssueKey>[] { new[] { K(1), K(2) }, new[] { K(3), K(4) } };
        Assert.Equal(1, IssueLayout.Crossings(graph, initial));

        var layout = IssueLayout.Compute(graph);
        Assert.Equal(0, IssueLayout.Crossings(graph, layout.Layers));
    }

    [Fact]
    public void LayoutDeGrafoVazio()
    {
        var layout = IssueLayout.Compute(IssueGraph.Build(Repo, Array.Empty<IssueData>()));

        Assert.Empty(layout.Layers);
    }

    // ── Leitura do GraphQL ──────────────────────────────────────────────────

    private const string PageJson = """
        {"data":{"repository":{
          "nameWithOwner":"Zimps/locanota",
          "issueTypes":{"nodes":[{"name":"Task"},{"name":"Bug"}]},
          "issues":{"pageInfo":{"hasNextPage":true,"endCursor":"abc"},"nodes":[
            {"number":1275,"title":"Pagamento","url":"https://github.com/Zimps/locanota/issues/1275",
             "issueType":{"name":"Bug","color":"RED"},"milestone":{"title":"v2"},
             "labels":{"nodes":[{"name":"backend","color":"A2EEEF"}]},
             "blockedBy":{"nodes":[
               {"number":1209,"title":"Base","url":"u","state":"OPEN","repository":{"nameWithOwner":"Zimps/locanota"}},
               {"number":7,"title":"Infra","url":"u","state":"CLOSED","repository":{"nameWithOwner":"Zimps/infra"}}]},
             "blocking":{"nodes":[]}}
          ]}}}}
        """;

    [Fact]
    public void LePaginaComTipoMilestoneLabelsEPontas()
    {
        var page = IssueGraphService.ParsePage(PageJson)!;

        Assert.Equal("Zimps/locanota", page.Repository);
        Assert.Equal(new[] { "Task", "Bug" }, page.Types);
        Assert.Equal("abc", page.EndCursor);

        var issue = Assert.Single(page.Issues);
        Assert.Equal(1275, issue.Number);
        Assert.Equal("Bug", issue.Type);
        Assert.Equal("#d1242f", issue.TypeColor);
        Assert.Equal("v2", issue.Milestone);
        Assert.Equal("#a2eeef", Assert.Single(issue.Labels).Color);
        Assert.Equal(2, issue.BlockedBy.Count);
        Assert.True(issue.BlockedBy[0].IsOpen);
        Assert.False(issue.BlockedBy[1].IsOpen);
        Assert.Equal("Zimps/infra", issue.BlockedBy[1].Repository);
    }

    [Fact]
    public void ContaPessoalNaoTemTiposEUltimaPaginaNaoTemCursor()
    {
        var page = IssueGraphService.ParsePage("""
            {"data":{"repository":{"nameWithOwner":"zote/Hypercode","issueTypes":null,
              "issues":{"pageInfo":{"hasNextPage":false,"endCursor":"x"},"nodes":[
                {"number":9,"title":"CI","url":"u","issueType":null,"milestone":null,"labels":{"nodes":[]},
                 "blockedBy":{"nodes":[]},"blocking":{"nodes":[]}}]}}}}
            """)!;

        Assert.Empty(page.Types);
        Assert.Null(page.EndCursor);
        Assert.Null(Assert.Single(page.Issues).Type);
    }

    [Fact]
    public void RespostaDeErroNaoViraPagina()
    {
        const string json = """{"data":{"repository":null},"errors":[{"message":"Could not resolve to a Repository"}]}""";

        Assert.Null(IssueGraphService.ParsePage(json));
        Assert.Equal("Could not resolve to a Repository", IssueGraphService.GraphQLError(json));
        Assert.Null(IssueGraphService.ParsePage("não é json"));
    }

    // ── View-model ──────────────────────────────────────────────────────────

    private sealed class FakeGitHub
    {
        public int Calls;
        public IssueGraphData Data = new(Repo, Array.Empty<IssueData>(), Array.Empty<string>(), DateTimeOffset.MinValue);

        public Task<IssueGraphData> LoadAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Data);
        }
    }

    private static (IssueGraphViewModel ViewModel, FakeGitHub GitHub, Func<DateTimeOffset> Advance) Open(
        IReadOnlyList<IssueData> issues,
        bool enabled = true,
        IReadOnlyList<string>? types = null)
    {
        var settings = new Settings { IssueGraphEnabled = enabled, IssueGraphPanelOpen = true };
        var github = new FakeGitHub { Data = new IssueGraphData(Repo, issues, types ?? Array.Empty<string>(), DateTimeOffset.MinValue) };
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var viewModel = new IssueGraphViewModel(settings, () => { }, github.LoadAsync, () => now);
        viewModel.SetRepository("/repo", "locanota");
        viewModel.Start();
        return (viewModel, github, () => now = now.AddMinutes(11));
    }

    [Fact]
    public void DesligadoNaoChamaOGitHub()
    {
        var (viewModel, github, _) = Open(Edges((1, 2)), enabled: false);

        viewModel.IsWindowOpen = true;
        viewModel.SetRepository("/outro", "outro");
        viewModel.RefreshNowAsync();

        Assert.Equal(0, github.Calls);
        Assert.False(viewModel.IsShown);
        Assert.False(viewModel.IsPanelVisible);
    }

    [Fact]
    public void AntesDoStartNaoChamaOGitHub()
    {
        var github = new FakeGitHub();
        var viewModel = new IssueGraphViewModel(new Settings { IssueGraphEnabled = true, IssueGraphPanelOpen = true }, () => { }, github.LoadAsync);

        viewModel.SetRepository("/repo", "repo");

        Assert.Equal(0, github.Calls);
    }

    [Fact]
    public void ListaOrdenaPorAlcanceEMarcaAsLivres()
    {
        // #1209 livre destrava a #1275, que destrava a #1278 e a #1277; #1275 presa por três.
        // A #3 vai mais longe: bloqueia a #1, que bloqueia a #1275.
        var (viewModel, github, _) = Open(Edges((1209, 1275), (1, 1275), (2, 1275), (1275, 1278), (1275, 1277), (3, 1)));

        Assert.Equal(1, github.Calls);
        var first = viewModel.Items[0];
        Assert.Equal(3, first.Number);
        Assert.Equal(4, first.Reach);

        var free = viewModel.Items.Single(item => item.Number == 1209);
        Assert.True(free.IsFree);
        Assert.Equal("destrava 3", free.ReachText);

        var stuck = viewModel.Items.Single(item => item.Number == 1275);
        Assert.False(stuck.IsFree);
        Assert.Equal("presa por 3", stuck.State);

        Assert.Equal(viewModel.Items.Select(item => item.Reach).OrderByDescending(r => r), viewModel.Items.Select(item => item.Reach));

        viewModel.OnlyFree = true;
        Assert.All(viewModel.Items, item => Assert.True(item.IsFree));
    }

    [Fact]
    public void FiltroDeMilestoneELabelValemParaListaEGrafo()
    {
        var (viewModel, _, _) = Open(new[]
        {
            Issue(1, blocks: new[] { 2 }, milestone: "v1", labels: new[] { "backend" }),
            Issue(2, blockedBy: new[] { 1 }, milestone: "v1"),
            Issue(3, blocks: new[] { 4 }, milestone: "v2"),
            Issue(4, blockedBy: new[] { 3 }, milestone: "v2"),
            Issue(5),
        });

        Assert.Equal(new[] { "Todas as milestones", "v1", "v2", "Sem milestone" }, viewModel.MilestoneOptions.Select(option => option.Label));
        Assert.Equal(4, viewModel.GraphNodes.Count);
        Assert.Equal("1 issue sem dependência aberta fica só na lista.", viewModel.GraphNote);

        viewModel.SelectedMilestone = viewModel.MilestoneOptions.Single(option => option.Value == "v1");
        Assert.Equal(new[] { 1, 2 }, viewModel.Items.Select(item => item.Number).OrderBy(n => n));
        Assert.Equal(new[] { 1, 2 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));

        viewModel.SelectedLabel = viewModel.LabelOptions.Single(option => option.Value == "backend");
        Assert.Equal(new[] { 1 }, viewModel.Items.Select(item => item.Number));

        viewModel.SelectedMilestone = viewModel.MilestoneOptions.Single(option => option.IsNone);
        Assert.True(viewModel.HasNoItems);
    }

    [Fact]
    public void FiltroDeTipoSomeSemTipos()
    {
        var (semTipos, _, _) = Open(Edges((1, 2)));
        Assert.False(semTipos.HasTypes);

        var (comTipos, _, _) = Open(new[] { Issue(1, type: "Bug"), Issue(2) }, types: new[] { "Task", "Bug" });
        Assert.True(comTipos.HasTypes);
        Assert.Equal(new[] { "Todos os tipos", "Bug", "Task", "Sem tipo" }, comTipos.TypeOptions.Select(option => option.Label));
    }

    [Fact]
    public void RepositorioSemDependenciaExplicaORecurso()
    {
        var (viewModel, _, _) = Open(new[] { Issue(1), Issue(2) });

        Assert.True(viewModel.HasNoDependencies);
        Assert.Empty(viewModel.GraphNodes);
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public void CicloApareceComoAviso()
    {
        var (viewModel, _, _) = Open(Edges((1, 2), (2, 1), (2, 3)));

        Assert.True(viewModel.HasCycles);
        Assert.Contains("#1 ↔ #2", viewModel.CycleWarning);
        Assert.Contains(viewModel.GraphEdges, edge => edge.IsCycle);
        Assert.All(viewModel.Items.Where(item => item.Number is 1 or 2), item => Assert.True(item.IsInCycle));
    }

    [Fact]
    public void FocoIsolaAVizinhancaEVolta()
    {
        var (viewModel, _, _) = Open(Edges((1, 2), (2, 3), (10, 11), (11, 12)));
        Assert.Equal(6, viewModel.GraphNodes.Count);

        viewModel.Focus(K(2));
        Assert.Equal(new[] { 1, 2, 3 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));
        Assert.True(viewModel.GraphNodes.Single(node => node.Key.Number == 2).IsFocused);
        Assert.NotNull(viewModel.FocusTitle);

        viewModel.ClearFocus();
        Assert.Equal(6, viewModel.GraphNodes.Count);
        Assert.False(viewModel.IsFocused);
    }

    /// <summary>
    /// #10 presa pela #5 (fechada) e pela #7 (aberta); #11 presa só pela #6 (fechada). As
    /// fechadas chegam só como ponta de dependência, como na leitura de verdade.
    /// </summary>
    private static IReadOnlyList<IssueData> AbertasEFechadas() => new[]
    {
        Issue(7, blocks: new[] { 10 }),
        Issue(10, blockedBy: new[] { 7 }) with { BlockedBy = new[] { Ref(5, open: false), Ref(7) } },
        Issue(11) with { BlockedBy = new[] { Ref(6, open: false) } },
    };

    private static void AssertMetricaSoContaAbertas(IssueGraphViewModel viewModel)
    {
        var items = viewModel.Items.ToDictionary(item => item.Number);
        Assert.Equal(new[] { 7, 10, 11 }, items.Keys.OrderBy(n => n));
        Assert.Equal(1, items[10].OpenBlockers);
        Assert.True(items[11].IsFree);
        Assert.True(items[7].IsFree);
        Assert.Equal(1, items[7].Reach);
    }

    [Fact]
    public void FechadasSomemDoGrafoPorPadraoComAsArestas()
    {
        var (viewModel, github, _) = Open(AbertasEFechadas());

        Assert.False(viewModel.ShowClosed);
        Assert.Equal(new[] { 7, 10 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));
        Assert.DoesNotContain(viewModel.GraphNodes, node => node.IsClosed);
        var edge = Assert.Single(viewModel.GraphEdges);
        Assert.False(edge.IsResolved);
        Assert.Equal("1 issue sem dependência aberta fica só na lista.", viewModel.GraphNote);
        AssertMetricaSoContaAbertas(viewModel);
        Assert.Equal(1, github.Calls);
    }

    [Fact]
    public void MostrarFechadasDesenhaSemMudarAMetricaNemLerDeNovo()
    {
        var (viewModel, github, _) = Open(AbertasEFechadas());

        viewModel.ShowClosed = true;

        Assert.Equal(new[] { 5, 6, 7, 10, 11 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));
        Assert.Equal(new[] { 5, 6 }, viewModel.GraphNodes.Where(node => node.IsClosed).Select(node => node.Key.Number).OrderBy(n => n));
        Assert.All(viewModel.GraphNodes.Where(node => node.IsClosed), node => Assert.True(node.IsStub));
        Assert.Equal(3, viewModel.GraphEdges.Count);
        Assert.Equal(2, viewModel.GraphEdges.Count(edge => edge.IsResolved));
        Assert.Null(viewModel.GraphNote);
        AssertMetricaSoContaAbertas(viewModel);
        Assert.True(viewModel.GraphNodes.Single(node => node.Key.Number == 11).IsFree);
        Assert.Equal(1, github.Calls);
    }

    [Fact]
    public void MostrarFechadasPersisteNasConfiguracoes()
    {
        var settings = new Settings { IssueGraphEnabled = true, IssueGraphPanelOpen = true };
        var saves = 0;
        var viewModel = new IssueGraphViewModel(settings, () => saves++, new FakeGitHub().LoadAsync);

        viewModel.ShowClosed = true;

        Assert.True(settings.IssueGraphShowClosed);
        Assert.Equal(1, saves);
        Assert.True(new IssueGraphViewModel(settings, () => { }, new FakeGitHub().LoadAsync).ShowClosed);
    }

    [Fact]
    public void EsconderFechadasTiraOFocoDeUmaFechada()
    {
        var (viewModel, _, _) = Open(AbertasEFechadas());
        viewModel.Focus(K(5));
        Assert.False(viewModel.IsFocused);

        viewModel.ShowClosed = true;
        viewModel.Focus(K(5));
        Assert.Equal(new[] { 5, 10 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));

        viewModel.ShowClosed = false;
        Assert.False(viewModel.IsFocused);
        Assert.Equal(new[] { 7, 10 }, viewModel.GraphNodes.Select(node => node.Key.Number).OrderBy(n => n));
    }

    [Fact]
    public void ArestaSaiDaDireitaDeQuemBloqueiaEEntraPelaEsquerdaDoBloqueado()
    {
        var (viewModel, _, _) = Open(Edges((1, 2)));

        var from = viewModel.GraphNodes.Single(node => node.Key.Number == 1);
        var to = viewModel.GraphNodes.Single(node => node.Key.Number == 2);
        var edge = Assert.Single(viewModel.GraphEdges);

        Assert.Equal(from.X + from.Width, edge.X1);
        Assert.Equal(to.X, edge.X2);
        Assert.True(to.X > from.X);
        Assert.True(viewModel.GraphWidth >= to.X + to.Width);
        Assert.Empty(edge.Via);
    }

    [Fact]
    public void ArestaLongaNaoPassaPorTrasDeCartao()
    {
        // 1→3 pula a coluna da 2; o vão por onde ela passa não encosta em cartão nenhum.
        var (viewModel, _, _) = Open(Edges((1, 2), (2, 3), (1, 3), (10, 2), (11, 2)));

        var edge = viewModel.GraphEdges.Single(e => e.Via.Count == 1);
        var waypoint = edge.Via[0];
        var middle = viewModel.GraphNodes.Where(node => node.X == waypoint.Left).ToList();
        Assert.NotEmpty(middle);
        Assert.All(middle, node => Assert.False(waypoint.Y >= node.Y && waypoint.Y <= node.Y + node.Height));
        Assert.Equal(viewModel.GraphNodes.Count, viewModel.GraphNodes.Select(node => node.Key).Distinct().Count());
    }

    [Fact]
    public void ReleSoQuandoALeituraFicaVelha()
    {
        var (viewModel, github, advance) = Open(Edges((1, 2)));
        Assert.Equal(1, github.Calls);

        viewModel.EnsureLoadedAsync();
        Assert.Equal(1, github.Calls);

        advance();
        viewModel.EnsureLoadedAsync();
        Assert.Equal(2, github.Calls);
    }

    [Fact]
    public void CadaRepositorioGuardaALeituraEOFiltro()
    {
        var (viewModel, github, _) = Open(new[] { Issue(1, milestone: "v1"), Issue(2, milestone: "v2") });
        viewModel.SelectedMilestone = viewModel.MilestoneOptions.Single(option => option.Value == "v1");

        viewModel.SetRepository("/outro", "outro");
        Assert.Equal(2, github.Calls);
        Assert.Equal("Todas as milestones", viewModel.SelectedMilestone?.Label);

        viewModel.SetRepository("/repo", "locanota");
        Assert.Equal(2, github.Calls);
        Assert.Equal("v1", viewModel.SelectedMilestone?.Value);
        Assert.Equal(new[] { 1 }, viewModel.Items.Select(item => item.Number));
    }

    [Fact]
    public void FalhaDaLeituraViraProblemaVisivel()
    {
        var settings = new Settings { IssueGraphEnabled = true, IssueGraphPanelOpen = true };
        var viewModel = new IssueGraphViewModel(settings, () => { },
            (_, _) => Task.FromException<IssueGraphData>(new InvalidOperationException("gh: não autenticado")));
        viewModel.SetRepository("/repo", "repo");
        viewModel.Start();

        Assert.Equal("gh: não autenticado", viewModel.Problem);
        Assert.False(viewModel.IsLoading);
    }
}
