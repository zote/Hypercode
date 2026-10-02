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

    /// <summary>Do menu da linha ou do cartão.</summary>
    private void OnOpenItemClick(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) _ = OpenAsync(item.Url);
    }

    /// <summary>Do menu da linha ou do cartão: isola a issue no grafo e mostra o grafo.</summary>
    private void OnFocusItemClick(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item || ViewModel is not { } viewModel) return;
        viewModel.Focus(item.Key);
        viewModel.ShowsGraph = true;
    }

    /// <summary>Do menu da linha ou do cartão: o diálogo de worktree já com o número da issue (#147).</summary>
    private void OnCreateWorktreeItemClick(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { External: false } item || ViewModel is not { } viewModel) return;
        viewModel.RequestWorktree(item.Key.Number);
    }

    /// <summary>A issue por trás de um item de menu: uma linha da lista ou um cartão do grafo.</summary>
    private static (IssueKey Key, string Url, bool External)? ItemOf(object? sender) => (sender as Control)?.DataContext switch
    {
        IssueImpactItem item => (item.Key, item.Url, false),
        GraphNodeItem node => (node.Key, node.Url, !node.CanCreateWorktree),
        _ => null,
    };

    private async Task OpenAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(uri);
    }
}
