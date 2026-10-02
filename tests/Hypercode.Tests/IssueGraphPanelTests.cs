using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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

        return Task.FromResult(new IssueGraphData(repo, new[]
        {
            Issue(1, Array.Empty<IssueRef>(), new[] { Ref(2) }),
            Issue(2, new[] { Ref(1) }, Array.Empty<IssueRef>()),
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
}
