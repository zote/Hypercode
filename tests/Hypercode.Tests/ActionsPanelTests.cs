using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Hypercode.Services;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>O painel da fila do Actions na janela de verdade, sem tela (#130): recolher, destacar e o que persiste.</summary>
public sealed class ActionsPanelTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    /// <summary>Sem repositório acompanhado e com o monitoramento desligado: nada chega a chamar o gh.</summary>
    private (MainWindow Window, Hypercode.ViewModels.MainViewModel Shell) Open(Settings settings)
    {
        var shell = _sandbox.OpenApp(settings);
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

    private static Control Panel(Window window) => window.FindControl<Border>("ActionsPanel")!;

    private static Settings Saved(GitSandbox sandbox) => SettingsStore.Parse(File.ReadAllText(sandbox.SettingsFile));

    [AvaloniaFact]
    public void BotaoDaBarraAbreERecolheOPainelEGuarda()
    {
        var (window, shell) = Open(new Settings());
        Assert.False(Panel(window).IsVisible);

        Click(window, "ActionsToggle");

        Assert.True(Panel(window).IsVisible);
        Assert.True(Saved(_sandbox).ActionsPanelOpen);

        Click(window, "ActionsToggle");

        Assert.False(Panel(window).IsVisible);
        Assert.False(Saved(_sandbox).ActionsPanelOpen);
        Assert.False(shell.Actions.IsPanelOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void PainelAbertoNaSessaoAnteriorVoltaAberto()
    {
        var (window, _) = Open(new Settings { ActionsPanelOpen = true });

        Assert.True(Panel(window).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void SemRepositorioOPainelExplicaOQueConfigurar()
    {
        var (window, shell) = Open(new Settings { ActionsPanelOpen = true });

        Assert.True(shell.Actions.HasNoRepositories);
        var hint = Panel(window).GetLogicalDescendantsOfType<TextBlock>()
            .FirstOrDefault(text => text.Text == "Nenhum repositório acompanhado");
        Assert.NotNull(hint);
        Assert.True(hint!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void JanelaPropriaSobreviveAoPainelEVoltaNaProximaSessao()
    {
        var (window, shell) = Open(new Settings { ActionsPanelOpen = true });

        window.ShowActionsWindow();
        Dispatcher.UIThread.RunJobs();
        var detached = window.ActionsWindow!;
        Assert.True(Saved(_sandbox).ActionsWindowOpen);

        // Recolher o painel não fecha a janela.
        shell.Actions.IsPanelOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(detached.IsVisible);
        Assert.Same(detached, window.ActionsWindow);

        // Fechar o app fecha a janela, mas ela fica guardada como aberta.
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(detached.IsVisible);
        Assert.True(Saved(_sandbox).ActionsWindowOpen);

        var (next, _) = Open(Saved(_sandbox));
        Assert.True(next.ActionsWindow is { IsVisible: true });
        next.Close();
    }

    [AvaloniaFact]
    public void FecharAJanelaPropriaTiraDoEstadoSalvo()
    {
        var (window, _) = Open(new Settings());

        window.ShowActionsWindow();
        Dispatcher.UIThread.RunJobs();
        window.ActionsWindow!.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Saved(_sandbox).ActionsWindowOpen);
        window.Close();
    }
}
