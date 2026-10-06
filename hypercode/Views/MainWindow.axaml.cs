using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

        WorktreeMenu = new WorktreeMenu(this, () => ViewModel, MenuTargets);
        WorktreeList.ContextMenu = WorktreeMenu.Menu;

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

        // Ao abrir e ao trocar de aba o foco vai para a lista (#100): as linhas chegam depois,
        // então a entrega espera a linha selecionada existir.
        _listFocusPending = true;
        DataContextChanged += (_, _) => WatchShell();
        WorktreeList.LayoutUpdated += (_, _) => DeliverListFocus();
        WorktreeList.SelectionChanged += (_, _) => DeliverListFocus();
    }

    private MainViewModel? _watchedShell;
    private bool _listFocusPending;

    private void WatchShell()
    {
        if (_watchedShell is not null)
        {
            _watchedShell.PropertyChanged -= OnShellPropertyChanged;
            _watchedShell.Issues.PropertyChanged -= OnIssuesPropertyChanged;
            _watchedShell.Issues.WorktreeRequested -= OnIssueWorktreeRequested;
        }

        _watchedShell = Shell;
        if (_watchedShell is not null)
        {
            _watchedShell.PropertyChanged += OnShellPropertyChanged;
            _watchedShell.Issues.PropertyChanged += OnIssuesPropertyChanged;
            _watchedShell.Issues.WorktreeRequested += OnIssueWorktreeRequested;
        }
    }

    /// <summary>Desligar o grafo nas configurações fecha a janela própria dele — mas ela volta se ligar de novo.</summary>
    private void OnIssuesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IssueGraphViewModel.IsEnabled) || Shell is not { } shell) return;

        if (!shell.Issues.IsEnabled && _issueGraphWindow is { } window)
        {
            _keepIssueGraphWindowState = true;
            window.Close();
            _keepIssueGraphWindowState = false;
        }
        else if (shell.Issues.IsEnabled && shell.Issues.IsWindowOpen)
        {
            ShowIssueGraphWindow();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedRepository)) return;
        _listFocusPending = true;
        Dispatcher.UIThread.Post(DeliverListFocus, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Põe o foco na linha selecionada — com a lista em foco a seleção fica na accent e as setas,
    /// o Enter e o menu já agem nela. Só toma o foco de ninguém ou da faixa de abas: no filtro
    /// (⌘F) ou em outro controle, ele fica onde o usuário o pôs.
    /// </summary>
    private void DeliverListFocus()
    {
        if (!_listFocusPending) return;

        var focused = FocusManager?.GetFocusedElement();
        if (focused is not null && !ReferenceEquals(focused, this) && !TabStrip.IsKeyboardFocusWithin)
        {
            _listFocusPending = false;
            return;
        }

        if (!WorktreeList.IsEffectivelyVisible || WorktreeList.SelectedIndex < 0) return;
        if (WorktreeList.ContainerFromIndex(WorktreeList.SelectedIndex) is not { } row) return;

        _listFocusPending = !row.Focus();
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
        if (Shell is not { } shell) return;

        // A janela da fila volta se estava aberta ao sair; a do grafo também, se ele está ligado.
        if (shell.Actions.IsWindowOpen) ShowActionsWindow();
        if (shell.Issues.IsEnabled && shell.Issues.IsWindowOpen) ShowIssueGraphWindow();

        // As abas da última sessão: a da frente carrega primeiro.
        await shell.OpenSavedAsync();
    }

    // ── Fila do GitHub Actions (#130) ───────────────────────────────────────

    private ActionsQueueWindow? _actionsWindow;

    /// <summary>A janela própria da fila, se aberta.</summary>
    internal ActionsQueueWindow? ActionsWindow => _actionsWindow;
    private bool _isClosingApp;

    private void OnToggleActionsPanelClick(object? sender, RoutedEventArgs e) => ToggleActionsPanel();

    private void OnCloseActionsPanelClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell) shell.Actions.IsPanelOpen = false;
    }

    /// <summary>
    /// ⇧⌘A e o botão da barra. Com a janela própria aberta e o painel recolhido, traz a janela
    /// para a frente em vez de abrir uma segunda view ao lado.
    /// </summary>
    private void ToggleActionsPanel()
    {
        if (Shell is not { } shell) return;

        if (!shell.Actions.IsPanelOpen && _actionsWindow is not null)
        {
            _actionsWindow.Activate();
            return;
        }

        shell.Actions.IsPanelOpen = !shell.Actions.IsPanelOpen;
    }

    /// <summary>Destacar: a fila vai para a janela própria e o painel recolhe.</summary>
    private void OnDetachActionsClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell) return;

        ShowActionsWindow();
        shell.Actions.IsPanelOpen = false;
    }

    /// <summary>
    /// Abre a janela da fila, ou a traz para a frente. Sem dono: num segundo monitor ela não
    /// flutua sobre a principal nem minimiza junto. Fechar a janela é o que a tira do estado
    /// salvo; fechar o app, não — ela volta na próxima sessão.
    /// </summary>
    internal void ShowActionsWindow()
    {
        if (Shell is not { } shell) return;

        if (_actionsWindow is not null)
        {
            _actionsWindow.Activate();
            return;
        }

        var window = new ActionsQueueWindow { DataContext = shell.Actions };
        window.Closed += (_, _) =>
        {
            _actionsWindow = null;
            if (!_isClosingApp) shell.Actions.IsWindowOpen = false;
        };

        _actionsWindow = window;
        shell.Actions.IsWindowOpen = true;
        window.Show();
    }

    // ── Grafo de issues (#144) ──────────────────────────────────────────────

    private IssueGraphWindow? _issueGraphWindow;

    /// <summary>Fechando a janela do grafo por desligar o recurso: ela fica guardada como aberta.</summary>
    private bool _keepIssueGraphWindowState;

    /// <summary>A janela própria do grafo, se aberta.</summary>
    internal IssueGraphWindow? IssueGraphWindow => _issueGraphWindow;

    private void OnToggleIssueGraphPanelClick(object? sender, RoutedEventArgs e) => ToggleIssueGraphPanel();

    private void OnCloseIssueGraphPanelClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell) shell.Issues.IsPanelOpen = false;
    }

    /// <summary>⇧⌘G e o botão da barra; como na fila, com a janela própria aberta e o painel recolhido, traz a janela.</summary>
    private void ToggleIssueGraphPanel()
    {
        if (Shell is not { } shell || !shell.Issues.IsEnabled) return;

        if (!shell.Issues.IsPanelOpen && _issueGraphWindow is not null)
        {
            _issueGraphWindow.Activate();
            return;
        }

        shell.Issues.IsPanelOpen = !shell.Issues.IsPanelOpen;
    }

    private void OnDetachIssueGraphClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell) return;

        ShowIssueGraphWindow();
        shell.Issues.IsPanelOpen = false;
    }

    /// <summary>Abre a janela do grafo, ou a traz para a frente. Sem dono, como a da fila.</summary>
    internal void ShowIssueGraphWindow()
    {
        if (Shell is not { } shell || !shell.Issues.IsEnabled) return;

        if (_issueGraphWindow is not null)
        {
            _issueGraphWindow.Activate();
            return;
        }

        var window = new IssueGraphWindow { DataContext = shell.Issues };
        window.Closed += (_, _) =>
        {
            _issueGraphWindow = null;
            if (!_isClosingApp && !_keepIssueGraphWindowState) shell.Issues.IsWindowOpen = false;
        };

        _issueGraphWindow = window;
        shell.Issues.IsWindowOpen = true;
        window.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        // As janelas da fila e do grafo sozinhas não seguram o app aberto.
        _isClosingApp = true;
        _issueGraphWindow?.Close();
        _actionsWindow?.Close();
        Shell?.Actions.Dispose();
        base.OnClosed(e);
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

            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                ToggleActionsPanel();
                break;

            case Key.G when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Shell is { Issues.IsEnabled: true }:
                e.Handled = true;
                ToggleIssueGraphPanel();
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

    // O X do filtro: limpa e deixa o cursor no campo, como o NSSearchField — mesmo quando o
    // foco estava na lista, já que o botão não recebe foco.
    private void OnClearFilterClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        viewModel.FilterText = string.Empty;
        this.FindControl<TextBox>("FilterBox")?.Focus();
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

    private async void OnCreateWorktreeClick(object? sender, RoutedEventArgs e) => await CreateWorktreeAsync(this);

    /// <summary>
    /// Do menu do grafo de issues (#147): o mesmo diálogo, já com o número. Pedido da janela
    /// destacada, o diálogo abre sobre ela; o grafo fica como estava, para continuar a escolha.
    /// </summary>
    private async void OnIssueWorktreeRequested(int number)
        => await CreateWorktreeAsync(_issueGraphWindow is { IsActive: true } detached ? detached : this, number);

    /// <summary>O diálogo aberto agora, se houver: a janela do grafo não é modal à principal, e daria para abrir dois.</summary>
    internal CreateWorktreeWindow? CreateWorktreeDialog { get; private set; }

    /// <summary>
    /// Novo worktree no repositório da aba da frente, com o contexto dele: raiz, barras, comando,
    /// autoatribuição e os worktrees de hoje. Com <paramref name="issueNumber"/>, a tela já nasce
    /// com ele no campo, e o setter busca a issue como se tivesse sido digitado.
    /// </summary>
    internal async Task CreateWorktreeAsync(Window owner, int? issueNumber = null)
    {
        if (Shell is not { } shell || ViewModel is not { } viewModel || viewModel.MainWorktreePath is not { } mainPath) return;
        if (CreateWorktreeDialog is { } open)
        {
            open.Activate();
            return;
        }

        var dialogViewModel = new CreateWorktreeViewModel(
            mainPath,
            shell.OpenTerminalAfterCreate,
            viewModel.EffectiveCommand,
            viewModel.AssignIssueOnCreate,
            viewModel.Effective.WorktreesRoot,
            viewModel.Effective.WorktreeFolderKeepsSlashes,
            viewModel.Worktrees.Select(row => row.Worktree).ToList(),
            viewModel.RememberWorktreeLayout);
        if (issueNumber is { } number) dialogViewModel.IssueNumberText = number.ToString(CultureInfo.InvariantCulture);

        var dialog = new CreateWorktreeWindow { DataContext = dialogViewModel };
        CreateWorktreeDialog = dialog;
        WorktreeCreationResult? result;
        try
        {
            result = await dialog.ShowDialog<WorktreeCreationResult?>(owner);
        }
        finally
        {
            CreateWorktreeDialog = null;
        }

        // O checkbox vale como preferência mesmo se o usuário cancelar depois de mexer nele.
        shell.OpenTerminalAfterCreate = dialogViewModel.OpenTerminal;
        dialogViewModel.Dispose();

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
        if (ViewModel is { } viewModel) await viewModel.OpenOrFocusAsync(viewModel.SelectedWorktree);
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } viewModel || RowOf(sender) is not { } row) return;

        e.Handled = true;
        viewModel.SelectedWorktree = row;
        await viewModel.OpenOrFocusAsync(row);
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

    // O menu é da lista, não da linha: age sobre a seleção inteira (#148: a mesma classe serve
    // ao grafo de issues).

    /// <summary>O menu de contexto da lista de worktrees.</summary>
    internal WorktreeMenu WorktreeMenu { get; }

    /// <summary>Linhas-alvo do menu, na ordem da lista.</summary>
    private IReadOnlyList<WorktreeRow> MenuTargets()
        => ViewModel?.SelectedRowsInViewOrder() ?? Array.Empty<WorktreeRow>();
}
