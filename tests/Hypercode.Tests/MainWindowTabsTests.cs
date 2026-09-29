using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Hypercode.ViewModels;
using Hypercode.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Hypercode.Tests.HeadlessApp))]

namespace Hypercode.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>A faixa de abas na janela de verdade, sem tela: teclado, mouse e arrastar pasta.</summary>
public sealed class MainWindowTabsTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    /// <summary>Abre a janela com esses repositórios salvos e espera todas as abas carregarem.</summary>
    private async Task<(MainWindow Window, MainViewModel Shell)> OpenAsync(params string[] repositories)
    {
        var settings = new Hypercode.Services.Settings { MonitorProfile = "off" };
        settings.Repositories.AddRange(repositories);
        var shell = _sandbox.OpenApp(settings);

        var window = new MainWindow { DataContext = shell };
        window.Show();

        await WaitUntilAsync(() => shell.Repositories.All(tab => tab.WorktreeCount > 0));
        return (window, shell);
    }

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

    private static ListBox TabStrip(Window window) => window.FindControl<ListBox>("TabStrip")!;

    private static Control Tab(Window window, int index) => TabStrip(window).ContainerFromIndex(index)!;

    private static Point Inside(Window window, Control control, double x)
        => control.TranslatePoint(new Point(x, control.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public async Task CmdNumeroTrocaDeAbaEAListaAcompanha()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web");
        var (window, shell) = await OpenAsync(api, web);

        Assert.Equal(2, TabStrip(window).ItemCount);
        var list = window.FindControl<ListBox>("WorktreeList")!;
        Assert.Same(shell.Repositories[0].VisibleWorktrees, list.ItemsSource);

        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        Assert.Same(shell.Repositories[1].VisibleWorktrees, list.ItemsSource);

        // Sem aba nessa posição, nada muda.
        window.KeyPressQwerty(PhysicalKey.Digit9, RawInputModifiers.Meta);
        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);

        window.Close();
    }

    [AvaloniaFact]
    public async Task VoltarParaAAbaMantemALinhaSelecionada()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web", "hotfix");
        var (window, shell) = await OpenAsync(api, web);
        var (first, second) = (shell.Repositories[0], shell.Repositories[1]);

        var feature = first.VisibleWorktrees.Single(row => row.Name == "api-feature");
        first.SelectedWorktree = feature;
        Dispatcher.UIThread.RunJobs();

        shell.SelectedRepository = second;
        Dispatcher.UIThread.RunJobs();
        shell.SelectedRepository = first;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(feature, first.SelectedWorktree);
        Assert.Same(feature, window.FindControl<ListBox>("WorktreeList")!.SelectedItem);
        window.Close();
    }

    private static ListBox WorktreeList(Window window) => window.FindControl<ListBox>("WorktreeList")!;

    private static TextBox FilterBox(Window window) => window.FindControl<TextBox>("FilterBox")!;

    /// <summary>O foco está na linha selecionada — é o que deixa a seleção na accent.</summary>
    private static void AssertSelectedRowFocused(Window window)
    {
        var list = WorktreeList(window);
        Assert.True(list.SelectedIndex >= 0);
        Assert.Same(list.ContainerFromIndex(list.SelectedIndex), window.FocusManager?.GetFocusedElement());
        Assert.True(list.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task AoAbrirOFocoVaiParaALinhaSelecionada()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var (window, _) = await OpenAsync(api);
        Dispatcher.UIThread.RunJobs();

        AssertSelectedRowFocused(window);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TrocarDeAbaLevaOFocoParaALista()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web", "hotfix");
        var (window, shell) = await OpenAsync(api, web);
        Dispatcher.UIThread.RunJobs();

        // Pelo atalho, com o foco na lista da aba de antes.
        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        AssertSelectedRowFocused(window);

        // Pelo clique na aba, que antes deixava o foco na faixa.
        var click = Inside(window, Tab(window, 0), 12);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(api, shell.SelectedRepository?.RepositoryPath);
        AssertSelectedRowFocused(window);

        window.Close();
    }

    [AvaloniaFact]
    public async Task CmdFContinuaLevandoAoFiltroETrocarDeAbaNaoOTira()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web", "hotfix");
        var (window, shell) = await OpenAsync(api, web);
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(FilterBox(window), window.FocusManager?.GetFocusedElement());

        // Quem está digitando no filtro e troca de aba continua no filtro.
        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        Assert.Same(FilterBox(window), window.FocusManager?.GetFocusedElement());

        // E o ↓ desce para a lista, como antes.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        AssertSelectedRowFocused(window);

        window.Close();
    }

    [AvaloniaFact]
    public async Task CmdWPedeConfirmacaoAntesDeFechar()
    {
        var api = _sandbox.CreateRepository("api");
        var web = _sandbox.CreateRepository("web");
        var (window, shell) = await OpenAsync(api, web);

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();

        var dialog = Assert.Single(window.OwnedWindows.OfType<ConfirmWindow>());
        Assert.Equal(2, shell.Repositories.Count);

        // Cancelar mantém a aba.
        dialog.Close(false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, shell.Repositories.Count);

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Meta);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(window.OwnedWindows.OfType<ConfirmWindow>()).Close(true);
        await WaitUntilAsync(() => shell.Repositories.Count == 1);

        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ArrastarAAbaMudaAOrdemEGrava()
    {
        var api = _sandbox.CreateRepository("api");
        var web = _sandbox.CreateRepository("web");
        var cli = _sandbox.CreateRepository("cli");
        var (window, shell) = await OpenAsync(api, web, cli);

        var start = Inside(window, Tab(window, 0), 12);
        var end = Inside(window, Tab(window, 2), Tab(window, 2).Bounds.Width - 12);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 20, start.Y));
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { web, cli, api }, shell.Repositories.Select(tab => tab.RepositoryPath));
        Assert.Equal(api, shell.SelectedRepository?.RepositoryPath);
        Assert.Equal(new[] { web, cli, api }, _sandbox.SavedSettings().Repositories);

        // Um clique sem arrastar só seleciona.
        var click = Inside(window, Tab(window, 0), 12);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { web, cli, api }, shell.Repositories.Select(tab => tab.RepositoryPath));
        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SoltarAPastaDoFinderAbreAAba()
    {
        var api = _sandbox.CreateRepository("api");
        var web = _sandbox.CreateRepository("web", "feature");
        var (window, shell) = await OpenAsync(api);

        // A pasta do worktree, como o Finder a entrega num arrastar.
        var folder = await window.StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.Combine(_sandbox.Root, "web-feature")));
        Assert.NotNull(folder);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(folder));
        var center = new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);

        window.DragDrop(center, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(center, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(center, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);

        await WaitUntilAsync(() => shell.Repositories.Count == 2 && shell.Repositories[1].WorktreeCount == 2);

        Assert.Equal(web, shell.SelectedRepository?.RepositoryPath);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ConfiguracoesNoEscopoDoRepositorioTravamOQueSegueOGlobal()
    {
        var api = _sandbox.CreateRepository("api");
        var (window, shell) = await OpenAsync(api);

        window.ShowSettings(shell.Repositories[0]);
        Dispatcher.UIThread.RunJobs();

        var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
        var command = settings.FindControl<TextBox>("CommandBox")!;
        var inherit = settings.GetLogicalDescendants().OfType<CheckBox>().First(box => box.Classes.Contains("inherit"));

        Assert.True(inherit.IsEffectivelyVisible);
        Assert.True(inherit.IsChecked);
        Assert.False(command.IsEffectivelyEnabled);

        // Desmarcar "usar o global" libera o campo, que passa a valer só para o repositório.
        inherit.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(command.IsEffectivelyEnabled);

        command.Text = "claude --resume";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("claude --resume", shell.Repositories[0].EffectiveCommand);
        Assert.Equal("claude", shell.Settings.Command);

        settings.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task SemAbaMostraOConviteParaAbrir()
    {
        var (window, shell) = await OpenAsync();

        Assert.True(shell.HasNoRepositories);
        Assert.False(window.FindControl<ListBox>("WorktreeList")!.IsEffectivelyVisible);
        window.Close();
    }
}
