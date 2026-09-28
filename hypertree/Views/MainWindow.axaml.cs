using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hypertree.Services;
using Hypertree.ViewModels;

namespace Hypertree.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;

        // Túnel: o ⌘F precisa funcionar mesmo com o foco dentro da lista ou de outro campo.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Linha associada ao controle que disparou o evento (item do template).</summary>
    private static WorktreeRow? RowOf(object? sender)
        => (sender as Control)?.DataContext as WorktreeRow;

    private async void OnOpened(object? sender, EventArgs e)
    {
        // Se havia um repositório salvo da última sessão, já carrega a lista.
        if (ViewModel is { } viewModel && !string.IsNullOrWhiteSpace(viewModel.RepositoryPath))
            await viewModel.LoadAsync();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Escolha o repositório git",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        viewModel.SetRepositoryPath(path);
        await viewModel.LoadAsync();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
        if (this.FindControl<TextBox>("FilterBox") is not { } filterBox) return;

        e.Handled = true;
        filterBox.Focus();
        filterBox.SelectAll();
    }

    // Esc limpa o filtro; ↓ desce para a lista, para escolher com o teclado e abrir com Enter.
    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        switch (e.Key)
        {
            case Key.Escape when viewModel.FilterText.Length > 0:
                e.Handled = true;
                viewModel.FilterText = string.Empty;
                break;
            case Key.Down:
            case Key.Enter or Key.Return:
                e.Handled = true;
                FocusSelectedRow();
                break;
        }
    }

    private void FocusSelectedRow()
    {
        if (this.FindControl<ListBox>("WorktreeList") is not { } list) return;
        if (list.SelectedIndex < 0 && list.ItemCount > 0) list.SelectedIndex = 0;
        if (list.SelectedIndex >= 0) list.ContainerFromIndex(list.SelectedIndex)?.Focus();
    }

    private void OnSortClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel
            && (sender as Control)?.Tag is string tag
            && Enum.TryParse<SortColumn>(tag, out var column))
        {
            viewModel.SortBy(column);
        }
    }

    private async void OnCreateWorktreeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.MainWorktreePath is not { } mainPath) return;

        var dialog = new CreateWorktreeWindow
        {
            DataContext = new CreateWorktreeViewModel(mainPath, viewModel.OpenTerminalAfterCreate, viewModel.EffectiveCommand),
        };

        var result = await dialog.ShowDialog<WorktreeCreationResult?>(this);

        // O checkbox vale como preferência mesmo se o usuário cancelar depois de mexer nele.
        if (dialog.DataContext is CreateWorktreeViewModel dialogViewModel)
            viewModel.OpenTerminalAfterCreate = dialogViewModel.OpenTerminal;

        if (result is not null)
            await viewModel.CompleteCreationAsync(result, viewModel.OpenTerminalAfterCreate);
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.LoadAsync();
    }

    private async void OnRepositoryPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        if (ViewModel is { } viewModel) await viewModel.LoadAsync();
    }

    private async void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        if (ViewModel is { } viewModel) await viewModel.LaunchAsync(viewModel.SelectedWorktree);
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } viewModel || RowOf(sender) is not { } row) return;

        e.Handled = true;
        viewModel.SelectedWorktree = row;
        await viewModel.LaunchAsync(row);
    }

    private async void OnHelpClick(object? sender, RoutedEventArgs e)
        => await new HelpWindow().ShowDialog(this);

    private async void OnCleanupClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var candidates = viewModel.CleanupCandidates();
        if (candidates.Count == 0)
        {
            viewModel.StatusMessage = "Nenhum worktree concluído para limpar.";
            return;
        }

        var headline = candidates.Count == 1
            ? "Remover 1 worktree concluído?"
            : $"Remover {candidates.Count} worktrees concluídos?";

        var confirmed = await new ConfirmWindow(
            "Limpar concluídos",
            headline + " A branch local não é tocada, só o worktree.",
            MainViewModel.BuildCleanupSummary(candidates),
            "Remover").ShowDialog<bool>(this);

        if (!confirmed) return;

        await viewModel.CleanupAsync(candidates);

        // O git recusa remover worktree com alteração não commitada — mostramos quais sobraram.
        if (viewModel.LastCleanupSkipped.Count > 0)
        {
            await new ConfirmWindow(
                "Worktrees mantidos",
                "Estes não foram removidos:",
                string.Join("\n\n", viewModel.LastCleanupSkipped),
                "Entendi").ShowDialog<bool>(this);
        }
    }

    private async void OnOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.LaunchAsync(viewModel.SelectedWorktree);
    }

    // Os itens do menu de contexto herdam o DataContext da linha clicada,
    // então agem sobre ela mesmo que a seleção não tenha mudado.
    private async void OnOpenTerminalMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.LaunchAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }

    private async void OnOpenShellMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.OpenShellAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }

    private async void OnResumeClaudeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.ResumeClaudeAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }

    private async void OnUpdateBranchMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.UpdateBranchAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }

    // Fetch e checagens primeiro, para o diálogo mostrar a base e quantos commits vêm; só
    // então a escolha entre merge e rebase. Recusa e conflito viram diálogo, não só rodapé.
    private async void OnUpdateFromBaseMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || (RowOf(sender) ?? viewModel.SelectedWorktree) is not { } row) return;
        if (!row.CanUpdateFromBase) return;

        var plan = await viewModel.PrepareBaseUpdateAsync(row);

        if (plan.Distance is not { } distance)
        {
            viewModel.StatusMessage = $"{row.Name} não foi atualizado";
            await new ConfirmWindow(
                "Atualizar a partir da base",
                $"Não dá para atualizar {row.Name} agora.",
                plan.Error ?? string.Empty,
                "Entendi").ShowDialog<bool>(this);
            return;
        }

        if (distance.Behind == 0)
        {
            viewModel.StatusMessage = $"{row.Branch} já contém {distance.Ref} — nada a trazer";
            return;
        }

        var strategy = await new UpdateFromBaseWindow(row, distance).ShowDialog<BaseUpdateStrategy?>(this);
        if (strategy is not { } chosen)
        {
            viewModel.StatusMessage = $"Atualização de {row.Branch} cancelada";
            return;
        }

        var outcome = await viewModel.UpdateFromBaseAsync(row, distance, chosen);
        if (outcome.Details is null) return;

        await new ConfirmWindow(
            "Atualizar a partir da base",
            outcome.Summary,
            outcome.Details,
            "Entendi").ShowDialog<bool>(this);
    }

    // Duas etapas: remove sem --force; se o git recusar (alteração não commitada, arquivo
    // não versionado), explica o motivo e só então oferece forçar — o que descarta o trabalho.
    private async void OnRemoveWorktreeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || (RowOf(sender) ?? viewModel.SelectedWorktree) is not { } row) return;
        if (!row.CanRemove) return;

        var confirmed = await new ConfirmWindow(
            "Apagar o worktree",
            $"Apagar o worktree {row.Name}? A branch local não é tocada, só o worktree.",
            MainViewModel.BuildRemovalSummary(row),
            "Apagar").ShowDialog<bool>(this);

        if (!confirmed) return;

        var error = await viewModel.RemoveWorktreeAsync(row);
        if (error is null || row.Worktree.IsPrunable || row.Worktree.IsLocked) return;

        var forced = await new ConfirmWindow(
            "Forçar a remoção",
            $"O git recusou apagar {row.Name}. Forçar descarta de vez as alterações não commitadas e os arquivos não versionados da pasta.",
            $"{error}\n\n{row.FullPath}",
            "Forçar e apagar").ShowDialog<bool>(this);

        if (forced) await viewModel.RemoveWorktreeAsync(row, force: true);
    }

    private async void OnRevealMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.RevealAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }

    private async void OnOpenPullRequestMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.OpenPullRequestAsync(RowOf(sender) ?? viewModel.SelectedWorktree);
    }
}
