using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hypercode.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // Esc por KeyDown, como no Sobre: IsCancel no botão fechava a janela trazida para a frente
    // pelo menu (Activate).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
