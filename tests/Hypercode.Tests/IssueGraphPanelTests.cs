using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hypercode.Services;
using Hypercode.ViewModels;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>O grafo de issues na janela de verdade, sem tela (#144): desligado some, ligado abre, segue a aba.</summary>
public sealed class IssueGraphPanelTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    private readonly List<string> _reads = new();

    /// <summary>#2 também presa por uma issue de outro repositório, que o grafo mostra como cartão.</summary>
    private bool _withExternal;

    public void Dispose() => _sandbox.Dispose();

    /// <summary>A leitura das issues anota o repositório e devolve #1 bloqueando #2.</summary>
    private Task<IssueGraphData> LoadAsync(string path, CancellationToken cancellationToken)
    {
        _reads.Add(path);
        const string repo = "zote/teste";
        IssueRef Ref(int number) => new(repo, number, $"issue {number}", $"https://github.com/{repo}/issues/{number}", true);
        IssueData Issue(int number, IssueRef[] blockedBy, IssueRef[] blocking)
            => new(repo, number, $"issue {number}", $"https://github.com/{repo}/issues/{number}", null, null, null,
                Array.Empty<IssueLabel>(), blockedBy, blocking);

        var external = new IssueRef("outro/repo", 9, "issue de fora", "https://github.com/outro/repo/issues/9", true);
        return Task.FromResult(new IssueGraphData(repo, new[]
        {
            Issue(1, Array.Empty<IssueRef>(), new[] { Ref(2) }),
            Issue(2, _withExternal ? new[] { Ref(1), external } : new[] { Ref(1) }, Array.Empty<IssueRef>()),
        }, Array.Empty<string>(), DateTimeOffset.UtcNow));
    }

    private (MainWindow Window, MainViewModel Shell) Open(Settings settings)
    {
        var shell = _sandbox.OpenApp(settings, LoadAsync);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    private static void Click(Window window, string name)
    {
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Panel(Window window) => window.FindControl<Border>("IssueGraphPanel")!;

    private static Button Toggle(Window window) => window.FindControl<Button>("IssueGraphToggle")!;

    [AvaloniaFact]
    public void DesligadoNaoTemBotaoNemPainelNemLeitura()
    {
        var repository = _sandbox.CreateRepository("repo");
        var (window, _) = Open(new Settings { Repositories = { repository }, IssueGraphPanelOpen = true, IssueGraphWindowOpen = true });

        Assert.False(Toggle(window).IsVisible);
        Assert.False(Panel(window).IsVisible);
        Assert.Null(window.IssueGraphWindow);
        Assert.Empty(_reads);
        window.Close();
    }

    [AvaloniaFact]
    public void LigarNasConfiguracoesMostraOBotaoEOPainelLe()
    {
        var repository = _sandbox.CreateRepository("repo");
        var (window, shell) = Open(new Settings { Repositories = { repository } });

        var settings = new SettingsViewModel(shell) { IssueGraphEnabled = true };
        Dispatcher.UIThread.RunJobs();
        Assert.True(Toggle(window).IsVisible);
        Assert.Empty(_reads);

        Click(window, "IssueGraphToggle");

        Assert.True(Panel(window).IsVisible);
        Assert.Equal(new[] { repository }, _reads);
        Assert.True(_sandbox.SavedSettings().IssueGraphEnabled);
        Assert.True(_sandbox.SavedSettings().IssueGraphPanelOpen);
        Assert.Equal(2, shell.Issues.Items.Count);
        Assert.Equal(1, shell.Issues.Items[0].Number);

        settings.IssueGraphEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Toggle(window).IsVisible);
        Assert.False(Panel(window).IsVisible);
        settings.Detach();
        window.Close();
    }

    [AvaloniaFact]
    public void TrocarDeAbaLeORepositorioDaFrente()
    {
        var first = _sandbox.CreateRepository("um");
        var second = _sandbox.CreateRepository("dois");
        var (window, shell) = Open(new Settings
        {
            Repositories = { first, second },
            SelectedRepository = first,
            IssueGraphEnabled = true,
            IssueGraphPanelOpen = true,
        });

        Assert.Equal(new[] { first }, _reads);

        shell.SelectIndex(1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { first, second }, _reads);
        Assert.Equal("dois", shell.Issues.RepositoryName);

        // Voltar à aba com a leitura fresca não relê.
        shell.SelectIndex(0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { first, second }, _reads);
        window.Close();
    }

    [AvaloniaFact]
    public void GrafoMostraCartoesEArestaENaoTemSeletorDeTipoSemTipos()
    {
        var repository = _sandbox.CreateRepository("repo");
        var (window, shell) = Open(new Settings { Repositories = { repository }, IssueGraphEnabled = true, IssueGraphPanelOpen = true });

        shell.Issues.ShowsGraph = true;
        Dispatcher.UIThread.RunJobs();

        var view = Panel(window).GetLogicalDescendants().OfType<IssueGraphView>().Single();
        Assert.False(view.FindControl<ComboBox>("TypeFilter")!.IsVisible);
        Assert.True(view.FindControl<ComboBox>("MilestoneFilter")!.IsVisible);
        Assert.Equal(2, shell.Issues.GraphNodes.Count);
        Assert.Single(shell.Issues.GraphEdges);
        window.Close();
    }

    [AvaloniaFact]
    public void JanelaPropriaSoComOGrafoLigado()
    {
        var repository = _sandbox.CreateRepository("repo");
        var (window, shell) = Open(new Settings { Repositories = { repository }, IssueGraphEnabled = true, IssueGraphPanelOpen = true });

        Click(window, "IssueGraphToggle");
        Assert.False(shell.Issues.IsPanelOpen);

        window.ShowIssueGraphWindow();
        Dispatcher.UIThread.RunJobs();
        var detached = window.IssueGraphWindow!;
        Assert.True(_sandbox.SavedSettings().IssueGraphWindowOpen);

        // Desligar fecha a janela, mas guarda que ela estava aberta: religar a traz de volta.
        var settings = new SettingsViewModel(shell) { IssueGraphEnabled = false };
        Dispatcher.UIThread.RunJobs();
        Assert.False(detached.IsVisible);
        Assert.True(_sandbox.SavedSettings().IssueGraphWindowOpen);

        settings.IssueGraphEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IssueGraphWindow is { IsVisible: true });

        settings.Detach();
        window.Close();
    }

    /// <summary>O worktree principal sai da lista de worktrees, lida do git fora da thread da UI.</summary>
    private static void WaitForWorktrees(MainViewModel shell)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (shell.SelectedRepository is not { CanCreateWorktree: true } && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.True(shell.SelectedRepository is { CanCreateWorktree: true });
    }

    private static CreateWorktreeViewModel DialogViewModel(MainWindow window)
        => Assert.IsType<CreateWorktreeViewModel>(window.CreateWorktreeDialog?.DataContext);

    [AvaloniaFact]
    public void MenuDoCartaoPedeWorktreeComONumeroEMantemOPainel()
    {
        _withExternal = true;
        var repository = _sandbox.CreateRepository("repo");
        var (window, shell) = Open(new Settings { Repositories = { repository }, IssueGraphEnabled = true, IssueGraphPanelOpen = true });
        shell.Issues.ShowsGraph = true;
        shell.Issues.Focus(new IssueKey("zote/teste", 2));
        WaitForWorktrees(shell);

        // O menu do cartão vem do ContextRequested (#148): o MenuFor é o que ele abre.
        var view = Panel(window).GetLogicalDescendants().OfType<IssueGraphView>().Single();
        MenuItem CreateItem(int number)
        {
            var node = shell.Issues.GraphNodes.Single(node => node.Key.Number == number);
            return view.MenuFor(node.Key, node.Url, external: !node.CanCreateWorktree).ItemsSource!.OfType<MenuItem>().Last();
        }

        // A de outro repositório: desabilitada, com o motivo no próprio item.
        var external = CreateItem(9);
        Assert.False(external.IsEnabled);
        Assert.Contains("outro repositório", external.Header as string);

        var local = CreateItem(2);
        Assert.True(local.IsEnabled);
        local.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var dialog = DialogViewModel(window);
        Assert.Equal("2", dialog.IssueNumberText);
        Assert.True(dialog.IsIssueMode);
        Assert.Same(window, window.CreateWorktreeDialog!.Owner);

        // Com o diálogo aberto, outro pedido não abre um segundo.
        var first = window.CreateWorktreeDialog;
        shell.Issues.RequestWorktree(1);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, window.CreateWorktreeDialog);

        // Cancelado, o grafo segue como estava: painel aberto, no grafo, com o foco.
        first!.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(window.CreateWorktreeDialog);
        Assert.True(shell.Issues.IsPanelOpen);
        Assert.True(shell.Issues.ShowsGraph);
        Assert.True(shell.Issues.IsFocused);
        window.Close();
    }

    // ── Issue com worktree (#148) ───────────────────────────────────────────

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("a janela não chegou ao estado esperado");
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static readonly IssueKey Issue1 = new("zote/teste", 1);
    private static readonly IssueKey Issue2 = new("zote/teste", 2);

    private static IssueImpactItem Item(MainViewModel shell, int number) => shell.Issues.Items.Single(item => item.Number == number);

    private static string?[] Headers(ContextMenu menu)
        => menu.ItemsSource!.OfType<MenuItem>().Select(item => item.Header as string).ToArray();

    /// <summary>Repositório com o worktree da #2 (pela branch), a janela aberta e o grafo lido e ligado.</summary>
    private async Task<(MainWindow Window, MainViewModel Shell, IssueGraphView View)> OpenWithWorktreeAsync()
    {
        var repository = _sandbox.CreateRepository("repo", "claude/issue-2-algo");
        var (window, shell) = Open(new Settings { Repositories = { repository }, IssueGraphEnabled = true, IssueGraphPanelOpen = true });
        await WaitUntilAsync(() => shell.Issues.Items.Count == 2 && Item(shell, 2).HasWorktree);
        var view = Panel(window).GetLogicalDescendants().OfType<IssueGraphView>().Single();
        return (window, shell, view);
    }

    [AvaloniaFact]
    public async Task IssueComWorktreeFicaMarcadaComAOrigem()
    {
        var (window, shell, _) = await OpenWithWorktreeAsync();

        Assert.False(Item(shell, 1).HasWorktree);
        Assert.Contains("pelo nome da branch", Item(shell, 2).WorktreeTip);
        Assert.Contains("repo-claude", shell.Issues.WorktreeFor(Issue2)!.Row.FullPath);

        shell.Issues.ShowsGraph = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Issues.GraphNodes.Single(node => node.Key == Issue2).HasWorktree);
        Assert.False(shell.Issues.GraphNodes.Single(node => node.Key == Issue1).HasWorktree);
        window.Close();
    }

    [AvaloniaFact]
    public void PedidoDaJanelaDestacadaAbreODialogoSobreEla()
    {
        var repository = _sandbox.CreateRepository("repo");
        var (window, shell) = Open(new Settings { Repositories = { repository }, IssueGraphEnabled = true });
        window.ShowIssueGraphWindow();
        Dispatcher.UIThread.RunJobs();
        var detached = window.IssueGraphWindow!;
        detached.Activate();
        WaitForWorktrees(shell);

        Assert.True(detached.IsActive);
        shell.Issues.RequestWorktree(1);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("1", DialogViewModel(window).IssueNumberText);
        Assert.Same(detached, window.CreateWorktreeDialog!.Owner);

        window.CreateWorktreeDialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(detached.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task BotaoDireitoNaIssueComWorktreeEOMenuDoWorktree()
    {
        var (window, shell, view) = await OpenWithWorktreeAsync();

        // A lista usa a mesma classe; configurada para a mesma linha, os itens têm de bater.
        Assert.Same(window.WorktreeMenu.Menu, window.FindControl<ListBox>("WorktreeList")!.ContextMenu);
        window.WorktreeMenu.Configure(new[] { shell.Issues.WorktreeFor(Issue2)!.Row });
        var fromList = window.WorktreeMenu.Menu.ItemsSource!.OfType<MenuItem>()
            .Select(item => (item.Header as string, item.IsEnabled, item.IsVisible)).ToArray();

        var fromGraph = view.MenuFor(Issue2, "https://github.com/zote/teste/issues/2").ItemsSource!.OfType<MenuItem>()
            .Select(item => (item.Header as string, item.IsEnabled, item.IsVisible)).ToArray();

        Assert.Equal(fromList, fromGraph[..fromList.Length]);
        Assert.Equal(new[] { "Focar no grafo", "Abrir a issue no navegador" }, fromGraph[fromList.Length..].Select(item => item.Item1));
        window.Close();
    }

    [AvaloniaFact]
    public async Task IssueSemWorktreeTemOMenuCurto()
    {
        var (window, _, view) = await OpenWithWorktreeAsync();

        Assert.Equal(new[] { "Focar no grafo", "Abrir a issue no navegador", IssueGraphViewModel.CreateWorktreeText },
            Headers(view.MenuFor(Issue1, "https://github.com/zote/teste/issues/1")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task RemoverOWorktreeDesmarcaAIssueNaHora()
    {
        var (window, shell, _) = await OpenWithWorktreeAsync();
        var row = shell.Issues.WorktreeFor(Issue2)!.Row;

        Assert.Null(await shell.SelectedRepository!.RemoveWorktreeAsync(row));
        Dispatcher.UIThread.RunJobs();

        Assert.False(Item(shell, 2).HasWorktree);
        Assert.Null(shell.Issues.WorktreeFor(Issue2));
        window.Close();
    }

    [AvaloniaFact]
    public async Task JanelaDestacadaTemOMesmoMenu()
    {
        var (window, _, _) = await OpenWithWorktreeAsync();

        window.ShowIssueGraphWindow();
        Dispatcher.UIThread.RunJobs();
        var detached = window.IssueGraphWindow!.GetLogicalDescendants().OfType<IssueGraphView>().Single();

        Assert.Contains("Apagar o worktree…", Headers(detached.MenuFor(Issue2, "https://github.com/zote/teste/issues/2")));
        window.Close();
    }
}
