using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class IssueGraphView : UserControl
{
    public IssueGraphView()
    {
        InitializeComponent();
    }

    private IssueGraphViewModel? ViewModel => DataContext as IssueGraphViewModel;

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.RefreshNowAsync();
    }

    private void OnShowListClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) viewModel.ShowsGraph = false;
    }

    private void OnShowGraphClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) viewModel.ShowsGraph = true;
    }

    private void OnClearFocusClick(object? sender, RoutedEventArgs e) => ViewModel?.ClearFocus();

    /// <summary>Esc solta o foco do grafo, onde quer que esteja o cursor dentro da view.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsFocused: true } viewModel)
        {
            e.Handled = true;
            viewModel.ClearFocus();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Clique foca a vizinhança do cartão; o segundo clique do duplo abre a issue.</summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GraphNodeItem node || ViewModel is not { } viewModel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        if (e.ClickCount >= 2) _ = OpenAsync(node.Url);
        else viewModel.Focus(node.Key);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IssueImpactItem item) _ = OpenAsync(item.Url);
    }

    private void OnItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not IssueImpactItem item) return;
        e.Handled = true;
        MenuFor(item.Key, item.Url).Open(control);
    }

    private void OnCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GraphNodeItem node) return;
        e.Handled = true;
        MenuFor(node.Key, node.Url, external: !node.CanCreateWorktree).Open(control);
    }

    /// <summary>
    /// O menu que o botão direito em <paramref name="key"/> abre. Já tem worktree (#148): o menu
    /// do worktree, o mesmo da lista, com os itens da issue no fim — a linha é lida a cada
    /// clique, porque a lista pode ter sido relida no meio. Senão, o curto da issue, com o
    /// Criar worktree (#147).
    /// </summary>
    internal ContextMenu MenuFor(IssueKey key, string url, bool external = false)
    {
        if (ViewModel is { } viewModel && viewModel.WorktreeFor(key) is not null)
        {
            var menu = new WorktreeMenu(
                this,
                () => ViewModel?.Repository,
                () => ViewModel?.WorktreeFor(key) is { } link ? new[] { link.Row } : Array.Empty<WorktreeRow>(),
                IssueMenuItems(key, url));
            if (menu.Prepare()) return menu.Menu;
        }

        var items = IssueMenuItems(key, url);
        items.Add(CreateWorktreeItem(key, external));
        return new ContextMenu { ItemsSource = items };
    }

    /// <summary>Focar no grafo e abrir a issue no navegador: os itens da issue, nos dois menus.</summary>
    private List<Control> IssueMenuItems(IssueKey key, string url)
    {
        var focus = new MenuItem { Header = "Focar no grafo" };
        focus.Click += (_, _) =>
        {
            if (ViewModel is not { } viewModel) return;
            viewModel.Focus(key);
            viewModel.ShowsGraph = true;
        };

        // "a issue": no menu do worktree há também o Abrir PR no navegador.
        var open = new MenuItem { Header = "Abrir a issue no navegador" };
        open.Click += (_, _) => _ = OpenAsync(url);

        return new List<Control> { focus, open };
    }

    /// <summary>
    /// O diálogo de worktree já com o número da issue (#147). Issue de outro repositório: o item
    /// fica desabilitado, com o motivo no texto — o tooltip de item desabilitado não aparece.
    /// </summary>
    private MenuItem CreateWorktreeItem(IssueKey key, bool external)
    {
        var item = new MenuItem
        {
            Header = external ? IssueGraphViewModel.CreateWorktreeExternalText : IssueGraphViewModel.CreateWorktreeText,
            IsEnabled = !external,
        };
        item.Click += (_, _) => ViewModel?.RequestWorktree(key.Number);
        return item;
    }

    private async Task OpenAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(uri);
    }
}
