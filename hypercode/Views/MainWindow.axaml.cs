using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;

        // O monitoramento desacelera com a janela em segundo plano e pausa minimizada.
        Activated += (_, _) => ReportActivity();
        Deactivated += (_, _) => ReportActivity();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) ReportActivity();
        };

        // Túnel: o ⌘F precisa funcionar mesmo com o foco dentro da lista ou de outro campo.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Linha associada ao controle que disparou o evento (item do template).</summary>
    private static WorktreeRow? RowOf(object? sender)
        => (sender as Control)?.DataContext as WorktreeRow;

    private void ReportActivity()
        => ViewModel?.SetWindowActivity(IsActive, WindowState == WindowState.Minimized);

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

    // O menu é da lista, não da linha: age sobre a seleção inteira. Com uma linha só, cada
    // item segue o caminho de sempre; com várias, vira lote e termina num relatório.

    /// <summary>Linhas-alvo do menu, na ordem da lista.</summary>
    private IReadOnlyList<WorktreeRow> MenuTargets()
        => ViewModel?.SelectedRowsInViewOrder() ?? Array.Empty<WorktreeRow>();

    private void OnListContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var rows = MenuTargets();
        if (rows.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        Configure(LaunchMenuItem, "Abrir no iTerm2 rodando o comando", rows, row => row.CanLaunch);
        Configure(ShellMenuItem, "Abrir o terminal", rows, row => row.CanLaunch);
        Configure(ResumeClaudeMenuItem, "Retomar a sessão do claude", rows, row => row.HasClaudeSession);
        Configure(RevealMenuItem, "Revelar no Finder", rows, _ => true);
        Configure(OpenPullRequestMenuItem, "Abrir PR no navegador", rows, row => row.HasPullRequest);
        Configure(MarkSeenMenuItem, "Marcar como visto", rows, row => row.HasPullRequestChanges);
        MarkSeenMenuItem.IsVisible = MarkSeenMenuItem.IsEnabled;
        Configure(UpdateBranchMenuItem, "Puxar do remoto (pull)", rows, row => row.CanUpdateBranch);
        Configure(RemoveMenuItem, rows.Count > 1 ? "Apagar os worktrees…" : "Apagar o worktree…", rows, row => row.CanRemove);

        // Cada item só aparece se alguma linha estiver no estado que ele muda: travar some
        // quando tudo já está travado, destravar some quando nada está.
        Configure(LockMenuItem, rows.Count > 1 ? "Travar os worktrees…" : "Travar o worktree…", rows, row => row.CanLock);
        LockMenuItem.IsVisible = LockMenuItem.IsEnabled;

        // Destravar passa por uma confirmação com os dados daquela trava: não vira lote.
        UnlockMenuItem.Header = "Destravar o worktree…";
        UnlockMenuItem.IsVisible = rows.Any(row => row.CanUnlock);
        UnlockMenuItem.IsEnabled = rows is [{ CanUnlock: true }];

        // Merge ou rebase é uma escolha por worktree, feita num diálogo: não vira lote.
        UpdateFromBaseMenuItem.Header = "Atualizar a partir da base…";
        UpdateFromBaseMenuItem.IsEnabled = rows.Count == 1 && rows[0].CanUpdateFromBase;
    }

    /// <summary>Com várias linhas, o rótulo diz em quantas a ação vale; basta uma para habilitar.</summary>
    private static void Configure(MenuItem item, string label, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports)
    {
        var count = rows.Count(supports);
        item.Header = rows.Count > 1 ? $"{label} ({count})" : label;
        item.IsEnabled = count > 0;
    }

    private async Task ShowBatchReportAsync(BatchOutcome outcome)
        => await new ConfirmWindow(
            outcome.Action,
            outcome.Summary,
            outcome.Report,
            "Entendi").ShowDialog<bool>(this);

    /// <summary>Uma janela por worktree: acima do limite, pergunta antes de abrir todas.</summary>
    private async Task<bool> ConfirmWindowsAsync(string what, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports)
    {
        var targets = rows.Where(supports).ToList();
        if (targets.Count <= MainViewModel.WindowConfirmationThreshold) return true;

        return await new ConfirmWindow(
            what,
            $"Abrir {targets.Count} janelas de uma vez?",
            string.Join("\n", targets.Select(row => $"{row.Name}  —  {row.Branch}")),
            $"Abrir {targets.Count}").ShowDialog<bool>(this);
    }

    /// <summary>Abrir janelas em lote: só há relatório se algo falhou ou ficou de fora.</summary>
    private async Task OpenManyAsync(
        string what,
        IReadOnlyList<WorktreeRow> rows,
        Func<WorktreeRow, bool> supports,
        Func<IReadOnlyList<WorktreeRow>, Task<BatchOutcome>> open)
    {
        if (!await ConfirmWindowsAsync(what, rows, supports)) return;

        var outcome = await open(rows);
        if (outcome.HasProblems) await ShowBatchReportAsync(outcome);
    }

    private async void OnOpenTerminalMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.LaunchAsync(rows[0]);
        else await OpenManyAsync("Abrir no iTerm2", rows, row => row.CanLaunch, viewModel.LaunchManyAsync);
    }

    private async void OnOpenShellMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.OpenShellAsync(rows[0]);
        else await OpenManyAsync("Abrir o terminal", rows, row => row.CanLaunch, viewModel.OpenShellManyAsync);
    }

    private async void OnResumeClaudeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.ResumeClaudeAsync(rows[0]);
        else await OpenManyAsync("Retomar a sessão do claude", rows, row => row.HasClaudeSession, viewModel.ResumeClaudeManyAsync);
    }

    private async void OnUpdateBranchMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1)
        {
            await viewModel.UpdateBranchAsync(rows[0]);
            return;
        }

        await ShowBatchReportAsync(await viewModel.UpdateBranchesAsync(rows));
    }

    // Fetch e checagens primeiro, para o diálogo mostrar a base e quantos commits vêm; só
    // então a escolha entre merge e rebase. Recusa e conflito viram diálogo, não só rodapé.
    private async void OnUpdateFromBaseMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || MenuTargets() is not [var row]) return;
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
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count > 1)
        {
            await RemoveManyAsync(viewModel, rows);
            return;
        }

        if (rows is not [var row] || !row.CanRemove) return;

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

    // Em lote é uma confirmação só, sem a segunda etapa de forçar: quem o git recusar fica,
    // e o relatório diz por quê.
    private async Task RemoveManyAsync(MainViewModel viewModel, IReadOnlyList<WorktreeRow> rows)
    {
        var targets = rows.Where(row => row.CanRemove).ToList();
        if (targets.Count == 0) return;

        var headline = $"Apagar {targets.Count} worktree(s)? A branch local não é tocada, só o worktree."
                       + (targets.Count < rows.Count ? $" O principal e o bare ficam de fora ({rows.Count - targets.Count})." : string.Empty);

        var confirmed = await new ConfirmWindow(
            "Apagar os worktrees",
            headline,
            MainViewModel.BuildBatchRemovalSummary(targets),
            $"Apagar {targets.Count}").ShowDialog<bool>(this);

        if (!confirmed) return;

        await ShowBatchReportAsync(await viewModel.RemoveWorktreesAsync(rows));
    }

    // Um motivo só, digitado uma vez, vale para todas as linhas da seleção que aceitam a trava.
    private async void OnLockMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var targets = MenuTargets().Where(row => row.CanLock).ToList();
        if (targets.Count == 0) return;

        var headline = targets is [var single]
            ? $"Travar o worktree {single.Name}?"
            : $"Travar {targets.Count} worktrees?";

        var reason = await new LockWorktreeWindow(
            headline,
            targets.Count == 1 ? "Travar" : $"Travar {targets.Count}").ShowDialog<string?>(this);

        if (reason is null) return;

        if (targets is [var row])
        {
            if (await viewModel.LockWorktreeAsync(row, reason) is { } error)
                await ShowLockErrorAsync("Travar o worktree", $"O git recusou travar {row.Name}.", error, row);
            return;
        }

        var outcome = await viewModel.LockWorktreesAsync(MenuTargets(), reason);
        if (outcome.Failed.Count > 0) await ShowBatchReportAsync(outcome);
    }

    // Sempre com confirmação, e com Cancelar como padrão: a trava pode ser de outra ferramenta.
    private async void OnUnlockMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || MenuTargets() is not [var row] || !row.CanUnlock) return;

        var confirmed = await new ConfirmWindow(
            "Destravar o worktree",
            row.Worktree.IsToolLock
                ? $"Destravar {row.Name}? A trava é do {row.Worktree.LockOwner}."
                : $"Destravar {row.Name}?",
            MainViewModel.BuildUnlockSummary(row),
            "Destravar",
            cancelIsDefault: true).ShowDialog<bool>(this);

        if (!confirmed) return;

        if (await viewModel.UnlockWorktreeAsync(row) is { } error)
            await ShowLockErrorAsync("Destravar o worktree", $"O git recusou destravar {row.Name}.", error, row);
    }

    private async Task ShowLockErrorAsync(string title, string headline, string error, WorktreeRow row)
        => await new ConfirmWindow(title, headline, $"{error}\n\n{row.FullPath}", "Entendi").ShowDialog<bool>(this);

    private void OnMarkSeenMenuClick(object? sender, RoutedEventArgs e)
        => ViewModel?.MarkPullRequestChangesSeen(MenuTargets());

    private async void OnRevealMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.RevealAsync(rows[0]);
        else if (await viewModel.RevealManyAsync(rows) is { HasProblems: true } outcome) await ShowBatchReportAsync(outcome);
    }

    private async void OnOpenPullRequestMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.OpenPullRequestAsync(rows[0]);
        else await OpenManyAsync("Abrir PR no navegador", rows, row => row.HasPullRequest, viewModel.OpenPullRequestsAsync);
    }
}
