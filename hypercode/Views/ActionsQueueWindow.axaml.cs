using Avalonia.Controls;
using Avalonia.Input;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class ActionsQueueWindow : Window
{
    public ActionsQueueWindow()
    {
        InitializeComponent();

        // Foco e minimizado contam para a cadência: num segundo monitor, a janela segue lendo
        // mesmo com a principal minimizada.
        Activated += (_, _) => ReportActivity();
        Deactivated += (_, _) => ReportActivity();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) ReportActivity();
        };
    }

    private void ReportActivity()
        => (DataContext as ActionsQueueViewModel)?.SetDetachedWindowActivity(IsActive, WindowState == WindowState.Minimized);

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
