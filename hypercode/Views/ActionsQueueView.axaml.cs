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
}
