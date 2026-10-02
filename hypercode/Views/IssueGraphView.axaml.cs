using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void OnOpenItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IssueImpactItem item) _ = OpenAsync(item.Url);
    }

    /// <summary>Do menu da linha: isola a issue no grafo e mostra o grafo.</summary>
    private void OnFocusItemClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not IssueImpactItem item || ViewModel is not { } viewModel) return;
        viewModel.Focus(item.Key);
        viewModel.ShowsGraph = true;
    }

    private async Task OpenAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(uri);
    }
}
