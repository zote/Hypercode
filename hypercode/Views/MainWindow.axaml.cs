using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
            if (e.Property == WindowStateProperty || e.Property == IsExtendedIntoWindowDecorationsProperty)
                UpdateTitleBarInset();
        };

        // A faixa de abas é a barra de título (#91): a área vazia dela arrasta a janela.
        // Borbulhamento sem handledEventsToo: clique em aba ou botão já chega tratado.
        TabStripBar.AddHandler(PointerPressedEvent, OnTitleBarPointerPressed, RoutingStrategies.Bubble);
        UpdateTitleBarInset();

        // Túnel: o ⌘F precisa funcionar mesmo com o foco dentro da lista ou de outro campo.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // Pasta arrastada do Finder abre numa aba.
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Arrastar a aba reordena. Com handledEventsToo: o ListBoxItem marca o clique como tratado.
        TabStrip.AddHandler(PointerPressedEvent, OnTabStripPointerPressed, RoutingStrategies.Tunnel);
        TabStrip.AddHandler(PointerMovedEvent, OnTabStripPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        TabStrip.AddHandler(PointerReleasedEvent, OnTabStripPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        TabStrip.AddHandler(PointerCaptureLostEvent, (_, _) => _draggedTab = null, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void UpdateTitleBarInset()
        => TabStripBar.Classes.Set("extended", NeedsTitleBarInset(IsExtendedIntoWindowDecorations, WindowState));

    /// <summary>
    /// A faixa recua para os botões de janela só com a barra estendida e fora da tela cheia, em
    /// que os botões somem.
    /// </summary>
    internal static bool NeedsTitleBarInset(bool extendedIntoDecorations, WindowState state)
        => extendedIntoDecorations && state != WindowState.FullScreen;

    // O AppKit só arrasta a janela por cliques nas views dele (toolbar, titlebar); a área vazia
    // da faixa é do Avalonia, então arrastar e o duplo-clique ficam por conta daqui.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsExtendedIntoWindowDecorations || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;

        if (e.ClickCount != 2)
        {
            BeginMoveDrag(e);
            return;
        }

        switch (TitleBarDoubleClick.Current())
        {
            case TitleBarDoubleClickAction.Zoom:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case TitleBarDoubleClickAction.Minimize:
                WindowState = WindowState.Minimized;
                break;
        }
    }

    private MainViewModel? Shell => DataContext as MainViewModel;

    /// <summary>O repositório da aba à frente: é sobre ele que a lista, o menu e o rodapé agem.</summary>
    private RepositoryViewModel? ViewModel => Shell?.SelectedRepository;

    /// <summary>Linha associada ao controle que disparou o evento (item do template).</summary>
    private static WorktreeRow? RowOf(object? sender)
        => (sender as Control)?.DataContext as WorktreeRow;

    /// <summary>Repositório da aba associada ao controle que disparou o evento (item do template ou do menu dele).</summary>
    private static RepositoryViewModel? TabOf(object? sender)
        => (sender as Control)?.DataContext as RepositoryViewModel;

    private void ReportActivity()
        => Shell?.SetWindowActivity(IsActive, WindowState == WindowState.Minimized);

    private async void OnOpened(object? sender, EventArgs e)
    {
        // As abas da última sessão: a da frente carrega primeiro.
        if (Shell is { } shell) await shell.OpenSavedAsync();
    }

    private async void OnAddRepositoryClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Escolha o repositório git",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) await AddRepositoryAsync(path);
    }

    /// <summary>Abre numa aba; se não der, diz por quê num diálogo — sem aba, não há rodapé para dizer.</summary>
    private async Task AddRepositoryAsync(string path)
    {
        if (Shell is not { } shell) return;

        if (await shell.AddRepositoryAsync(path) is { } error)
            await ConfirmWindow.Notice("Abrir repositório", "Não dá para abrir essa pasta.", error).ShowDialog<bool>(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Uma aba por pasta solta; o que não for repositório git vira diálogo, um por vez.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is null) return;

        foreach (var path in paths) await AddRepositoryAsync(path);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;

        switch (e.Key)
        {
            case Key.F when this.FindControl<TextBox>("FilterBox") is { IsEffectivelyVisible: true } filterBox:
                e.Handled = true;
                filterBox.Focus();
                filterBox.SelectAll();
                break;

            case Key.W when ViewModel is { } repository:
                e.Handled = true;
                _ = ConfirmCloseTabAsync(repository);
                break;

            case >= Key.D1 and <= Key.D9:
                e.Handled = true;
                Shell?.SelectIndex(e.Key - Key.D1);
                break;
        }
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (TabOf(sender) is { } repository) _ = ConfirmCloseTabAsync(repository);
    }

    /// <summary>Fechar a aba pede confirmação e deixa claro que o disco não é tocado.</summary>
    private async Task ConfirmCloseTabAsync(RepositoryViewModel repository)
    {
        if (Shell is not { } shell) return;

        var confirmed = await new ConfirmWindow(
            "Fechar a aba",
            $"Fechar {repository.DisplayName}?",
            "O repositório sai da faixa de abas e deixa de ser monitorado: sem watcher, sem consulta ao GitHub, "
            + "sem limpeza automática.\n\nNada no disco é apagado — nem worktree, nem branch, nem arquivo. A carência da "
            + "limpeza, a memória dos PRs e as configurações deste repositório ficam guardadas para quando ele for reaberto.\n\n"
            + repository.RepositoryPath,
            "Fechar a aba").ShowDialog<bool>(this);

        if (confirmed) shell.CloseRepository(repository);
    }

    private void OnTabSettingsClick(object? sender, RoutedEventArgs e) => ShowSettings(TabOf(sender));

    private async void OnTabRevealClick(object? sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is not { } repository) return;

        try
        {
            await TerminalLauncher.RevealInFinderAsync(repository.RepositoryPath);
        }
        catch (Exception exception)
        {
            repository.StatusMessage = exception.Message;
        }
    }

    // Arrastar a aba: depois de alguns pixels, a aba vai para a posição sob o ponteiro. A
    // captura passa para a faixa, porque o item arrastado é recriado a cada troca de posição.
    private RepositoryViewModel? _draggedTab;
    private Point _dragStart;
    private bool _isDraggingTab;

    private const double TabDragThreshold = 6;

    private void OnTabStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDraggingTab = false;
        _draggedTab = e.GetCurrentPoint(TabStrip).Properties.IsLeftButtonPressed && e.Source is Visual source
            ? source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as RepositoryViewModel
            : null;
        _dragStart = e.GetPosition(TabStrip);
    }

    private void OnTabStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedTab is not { } dragged || Shell is not { } shell) return;

        var position = e.GetPosition(TabStrip);
        if (!_isDraggingTab)
        {
            if (Math.Abs(position.X - _dragStart.X) < TabDragThreshold) return;
            _isDraggingTab = true;
            e.Pointer.Capture(TabStrip);
        }

        for (var index = 0; index < shell.Repositories.Count; index++)
        {
            if (TabStrip.ContainerFromIndex(index) is not { } container) continue;
            if (container.TranslatePoint(default, TabStrip) is not { } origin) continue;

            if (position.X >= origin.X && position.X < origin.X + container.Bounds.Width)
            {
                if (!ReferenceEquals(shell.Repositories[index], dragged)) shell.MoveRepository(dragged, index);
                break;
            }
        }
    }

    private void OnTabStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingTab) e.Pointer.Capture(null);
        _draggedTab = null;
        _isDraggingTab = false;
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
        if (Shell is not { } shell || ViewModel is not { } viewModel || viewModel.MainWorktreePath is not { } mainPath) return;

        var dialog = new CreateWorktreeWindow
        {
            DataContext = new CreateWorktreeViewModel(mainPath, shell.OpenTerminalAfterCreate, viewModel.EffectiveCommand, viewModel.AssignIssueOnCreate),
        };

        var result = await dialog.ShowDialog<WorktreeCreationResult?>(this);

        // O checkbox vale como preferência mesmo se o usuário cancelar depois de mexer nele.
        if (dialog.DataContext is CreateWorktreeViewModel dialogViewModel)
            shell.OpenTerminalAfterCreate = dialogViewModel.OpenTerminal;

        if (result is not null)
            await viewModel.CompleteCreationAsync(result, shell.OpenTerminalAfterCreate);
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
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

    private SettingsWindow? _settingsWindow;

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>
    /// Abre as configurações, ou traz para a frente as que já estão abertas. Não é modal: o
    /// perfil do monitor muda com a lista à vista, e o ⌘, do menu do app chega aqui também.
    /// Com um repositório (menu da aba), já abre no escopo dele; sem, no global.
    /// </summary>
    public void ShowSettings(RepositoryViewModel? scope = null)
    {
        if (Shell is not { } shell) return;

        if (_settingsWindow is not null)
        {
            if (scope is not null && _settingsWindow.DataContext is SettingsViewModel open)
                open.SelectedScope = open.Scopes.FirstOrDefault(item => ReferenceEquals(item.Repository, scope)) ?? open.SelectedScope;
            _settingsWindow.Activate();
            return;
        }

        var settings = new SettingsViewModel(shell, scope);
        _settingsWindow = new SettingsWindow { DataContext = settings };
        _settingsWindow.Closed += (_, _) =>
        {
            settings.Detach();
            _settingsWindow = null;
        };
        _settingsWindow.Show(this);
    }

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
            RepositoryViewModel.BuildCleanupSummary(candidates),
            "Remover",
            ConfirmStyle.Destructive).ShowDialog<bool>(this);

        if (!confirmed) return;

        await viewModel.CleanupAsync(candidates);

        // O git recusa remover worktree com alteração não commitada — mostramos quais sobraram.
        if (viewModel.LastCleanupSkipped.Count > 0)
        {
            await ConfirmWindow.Notice(
                "Worktrees mantidos",
                "Estes não foram removidos:",
                string.Join("\n\n", viewModel.LastCleanupSkipped)).ShowDialog<bool>(this);
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
        Configure(RerunFailedChecksMenuItem, "Rodar novamente os checks que falharam", rows, row => row.CanRerunFailedChecks);
        RerunFailedChecksMenuItem.IsVisible = RerunFailedChecksMenuItem.IsEnabled;
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
        => await ConfirmWindow.Notice(
            outcome.Action,
            outcome.Summary,
            outcome.Report).ShowDialog<bool>(this);

    /// <summary>Uma janela por worktree: acima do limite, pergunta antes de abrir todas.</summary>
    private async Task<bool> ConfirmWindowsAsync(string what, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports)
    {
        var targets = rows.Where(supports).ToList();
        if (targets.Count <= RepositoryViewModel.WindowConfirmationThreshold) return true;

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
            await ConfirmWindow.Notice(
                "Atualizar a partir da base",
                $"Não dá para atualizar {row.Name} agora.",
                plan.Error ?? string.Empty).ShowDialog<bool>(this);
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

        await ConfirmWindow.Notice(
            "Atualizar a partir da base",
            outcome.Summary,
            outcome.Details).ShowDialog<bool>(this);
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
            RepositoryViewModel.BuildRemovalSummary(row),
            "Apagar",
            ConfirmStyle.Destructive).ShowDialog<bool>(this);

        if (!confirmed) return;

        var error = await viewModel.RemoveWorktreeAsync(row);
        if (error is null || row.Worktree.IsPrunable || row.Worktree.IsLocked) return;

        var forced = await new ConfirmWindow(
            "Forçar a remoção",
            $"O git recusou apagar {row.Name}. Forçar descarta de vez as alterações não commitadas e os arquivos não versionados da pasta.",
            $"{error}\n\n{row.FullPath}",
            "Forçar e apagar",
            ConfirmStyle.Destructive).ShowDialog<bool>(this);

        if (forced) await viewModel.RemoveWorktreeAsync(row, force: true);
    }

    // Em lote é uma confirmação só, sem a segunda etapa de forçar: quem o git recusar fica,
    // e o relatório diz por quê.
    private async Task RemoveManyAsync(RepositoryViewModel viewModel, IReadOnlyList<WorktreeRow> rows)
    {
        var targets = rows.Where(row => row.CanRemove).ToList();
        if (targets.Count == 0) return;

        var headline = $"Apagar {targets.Count} worktree(s)? A branch local não é tocada, só o worktree."
                       + (targets.Count < rows.Count ? $" O principal e o bare ficam de fora ({rows.Count - targets.Count})." : string.Empty);

        var confirmed = await new ConfirmWindow(
            "Apagar os worktrees",
            headline,
            RepositoryViewModel.BuildBatchRemovalSummary(targets),
            $"Apagar {targets.Count}",
            ConfirmStyle.Destructive).ShowDialog<bool>(this);

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
            RepositoryViewModel.BuildUnlockSummary(row),
            "Destravar",
            ConfirmStyle.CancelIsDefault).ShowDialog<bool>(this);

        if (!confirmed) return;

        if (await viewModel.UnlockWorktreeAsync(row) is { } error)
            await ShowLockErrorAsync("Destravar o worktree", $"O git recusou destravar {row.Name}.", error, row);
    }

    private async Task ShowLockErrorAsync(string title, string headline, string error, WorktreeRow row)
        => await ConfirmWindow.Notice(title, headline, $"{error}\n\n{row.FullPath}").ShowDialog<bool>(this);

    private void OnMarkSeenMenuClick(object? sender, RoutedEventArgs e)
        => ViewModel?.MarkPullRequestChangesSeen(MenuTargets());

    private async void OnRerunFailedChecksMenuClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.RerunFailedChecksAsync(rows[0]);
        else await ShowBatchReportAsync(await viewModel.RerunFailedChecksManyAsync(rows));
    }

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
