using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Views;

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
        if (ViewModel is not { } viewModel) return;
        FocusModeField();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        await viewModel.InitializeAsync();
    }

    // Trocar de modo leva o foco ao primeiro campo dele — depois do layout, quando ele já está visível.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A troca notifica os três modos; reage só ao que ficou marcado, para focar uma vez.
        var becameActive = (e.PropertyName, ViewModel) switch
        {
            (nameof(CreateWorktreeViewModel.IsIssueMode), { IsIssueMode: true }) => true,
            (nameof(CreateWorktreeViewModel.IsPullRequestMode), { IsPullRequestMode: true }) => true,
            (nameof(CreateWorktreeViewModel.IsNewBranchMode), { IsNewBranchMode: true }) => true,
            _ => false,
        };
        if (becameActive) FocusModeField();
    }

    private void FocusModeField()
    {
        var field = ViewModel switch
        {
            { IsIssueMode: true } => "IssueBox",
            { IsPullRequestMode: true } => "PullRequestBox",
            { IsNewBranchMode: true } => "BranchNameBox",
            _ => null,
        };
        if (field is not null) Dispatcher.UIThread.Post(() => this.FindControl<TextBox>(field)?.Focus());
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
