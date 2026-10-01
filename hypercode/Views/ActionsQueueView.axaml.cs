using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class ActionsQueueView : UserControl
{
    public ActionsQueueView()
    {
        InitializeComponent();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ActionsQueueViewModel viewModel) await viewModel.RefreshNowAsync();
    }

    /// <summary>As configurações são da janela principal, esteja a view nela ou na janela própria.</summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var main = this.FindAncestorOfType<MainWindow>()
                   ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        main?.ShowSettings();
    }

    // ── Menu do run (#133) ──────────────────────────────────────────────────

    /// <summary>
    /// O menu é montado a cada abertura, com o alvo da leitura que está na tela: as listas se
    /// refazem a cada ciclo, e um menu preso ao item antigo agiria sobre um estado que já passou.
    /// Runner livre não tem run, e não tem menu.
    /// </summary>
    private void OnRunContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control) return;

        var run = control.DataContext switch
        {
            RunnerItem item => item.Run,
            QueuedJobItem item => item.Run,
            WaitingRunItem item => item.Run,
            _ => null,
        };
        if (run is null) return;

        e.Handled = true;
        var menu = new ContextMenu { ItemsSource = RunMenuItems(run) };
        if (DataContext is ActionsQueueViewModel viewModel)
        {
            menu.Opened += (_, _) => viewModel.IsMenuOpen = true;
            menu.Closed += (_, _) => viewModel.IsMenuOpen = false;
        }

        menu.Open(control);
    }

    private List<Control> RunMenuItems(RunTarget run)
    {
        var items = new List<Control>();

        // O motivo do bloqueio vai como primeira linha, à vista — a dica de item desabilitado
        // não aparece em todo lugar, e o motivo é justamente o que a pessoa precisa ler.
        if (run.CancelBlocked is { } reason)
        {
            items.Add(new MenuItem { Header = reason, IsEnabled = false });
            items.Add(new Separator());
        }

        var cancel = new MenuItem
        {
            Header = "Cancelar o run inteiro…",
            IsEnabled = run.CanCancel,
        };
        ToolTip.SetTip(cancel, "POST /actions/runs/{id}/cancel: para todos os jobs do run, os que rodam e os que esperam. O GitHub não cancela um job sozinho. Os passos com if: always() ainda rodam.");
        cancel.Click += async (_, _) => await CancelAsync(run, force: false);
        items.Add(cancel);

        var force = new MenuItem
        {
            Header = "Forçar cancelamento…",
            IsEnabled = run.CanForceCancel,
        };
        ToolTip.SetTip(force, "POST /actions/runs/{id}/force-cancel: para o run sem rodar os passos com if: always() nem a limpeza do workflow. Para o run que não para com o cancelamento comum.");
        force.Click += async (_, _) => await CancelAsync(run, force: true);
        items.Add(force);

        if (run.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            items.Add(new Separator());
            var open = new MenuItem { Header = "Abrir o run no navegador" };
            open.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(uri);
            };
            items.Add(open);
        }

        return items;
    }

    private async Task CancelAsync(RunTarget run, bool force)
    {
        if (DataContext is not ActionsQueueViewModel viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;

        var (title, headline, body, confirm) = ActionsQueueViewModel.CancelConfirmation(run, force);
        var confirmed = await new ConfirmWindow(title, headline, body, confirm, ConfirmStyle.Destructive, "Não cancelar")
            .ShowDialog<bool>(owner);
        if (!confirmed) return;

        if (await viewModel.CancelRunAsync(run, force) is { } problem)
            await ConfirmWindow.Notice(title, $"Não deu para cancelar o run {run.Name}.", problem).ShowDialog<bool>(owner);
    }
}
