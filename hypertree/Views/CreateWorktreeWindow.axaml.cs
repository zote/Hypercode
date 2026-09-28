using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hypertree.Services;
using Hypertree.ViewModels;

namespace Hypertree.Views;

/// <summary>Fecha devolvendo o <see cref="WorktreeCreationResult"/>, ou null se cancelado.</summary>
public partial class CreateWorktreeWindow : Window
{
    public CreateWorktreeWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private CreateWorktreeViewModel? ViewModel => DataContext as CreateWorktreeViewModel;

    private async void OnOpened(object? sender, EventArgs e)
    {
        this.FindControl<TextBox>("BranchNameBox")?.Focus();
        if (ViewModel is { } viewModel) await viewModel.InitializeAsync();
    }

    // Enter num campo cria, se já dá para criar; o IsDefault do botão não alcança o AutoCompleteBox.
    private async void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || ViewModel is not { CanCreate: true }) return;
        e.Handled = true;
        await CreateAsync();
    }

    private async void OnCreate(object? sender, RoutedEventArgs e) => await CreateAsync();

    private async Task CreateAsync()
    {
        if (ViewModel is not { } viewModel) return;
        if (await viewModel.CreateAsync() is { } result) Close(result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
