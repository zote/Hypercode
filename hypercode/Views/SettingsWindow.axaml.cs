using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // A lista da fila do Actions grava ao sair do campo; fechar com o cursor nele também conta.
        Closing += (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel && ActionsRepositoriesBox.Text is { } text)
                viewModel.ActionsRepositoriesText = text;
        };
    }


    /// <summary>
    /// Ligar a limpeza automática pede consentimento: o aviso diz o que o git apaga junto e o
    /// usuário escolhe. Desligar não pergunta nada. O checkbox volta ao que o view model diz
    /// até a resposta — é ele quem grava.
    /// </summary>
    private async void OnAutoCleanupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;

        var wanted = AutoCleanupBox.IsChecked == true;
        AutoCleanupBox.SetCurrentValue(ToggleButton.IsCheckedProperty, viewModel.AutoCleanup);

        if (!wanted)
        {
            viewModel.AutoCleanup = false;
            return;
        }

        var confirmed = await new ConfirmWindow(
            "Limpeza automática",
            RepositoryViewModel.AutoCleanupWarningTitle,
            viewModel.AutoCleanupWarning,
            "Ativar mesmo assim",
            ConfirmStyle.CancelIsDefault).ShowDialog<bool>(this);

        if (confirmed) viewModel.AutoCleanup = true;
    }

    private async void OnAutoCleanupInfoClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;

        await ConfirmWindow.Notice(
            "Limpeza automática",
            "O que a limpeza automática faz — e o que ela apaga junto",
            viewModel.AutoCleanupWarning).ShowDialog<bool>(this);
    }

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
