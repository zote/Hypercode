using Avalonia.Controls;
using Avalonia.Input;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class IssueGraphWindow : Window
{
    public IssueGraphWindow()
    {
        InitializeComponent();

        // Voltar à janela com a leitura velha relê.
        Activated += (_, _) => ReportActivity();
        Deactivated += (_, _) => ReportActivity();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) ReportActivity();
        };
    }

    private void ReportActivity()
        => (DataContext as IssueGraphViewModel)?.SetDetachedWindowActivity(IsActive, WindowState == WindowState.Minimized);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
