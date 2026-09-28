using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hypercode.Views;

/// <summary>
/// Pede o motivo da trava, que é opcional. Devolve o texto digitado (vazio = sem motivo),
/// ou null se o usuário cancelar.
/// </summary>
public partial class LockWorktreeWindow : Window
{
    public LockWorktreeWindow()
    {
        InitializeComponent();
    }

    public LockWorktreeWindow(string headline, string confirmLabel) : this()
    {
        HeadlineText.Text = headline;
        ConfirmButton.Content = confirmLabel;
        Opened += (_, _) => ReasonBox.Focus();
    }

    private void OnReasonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        Confirm();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm() => Close((ReasonBox.Text ?? string.Empty).Trim());
}
