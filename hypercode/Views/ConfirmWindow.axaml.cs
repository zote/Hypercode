using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hypercode.Views;

/// <summary>Qual botão o Return aciona, e se a ação é destrutiva (HIG — Alerts).</summary>
public enum ConfirmStyle
{
    /// <summary>Return confirma.</summary>
    Default,

    /// <summary>Return cancela: para ações em que o engano custa mais que o clique extra.</summary>
    CancelIsDefault,

    /// <summary>
    /// Apaga ou descarta algo: o botão de confirmar sai em vermelho e o Return cancela — a HIG
    /// pede que a ação destrutiva nunca seja a padrão.
    /// </summary>
    Destructive,
}

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string headline, string body, string confirmLabel, ConfirmStyle style = ConfirmStyle.Default)
        : this()
    {
        Title = title;
        Frame.Headline = headline;
        BodyText.Text = body;
        BodyBox.IsVisible = !string.IsNullOrWhiteSpace(body);
        ConfirmButton.Content = confirmLabel;

        if (style == ConfirmStyle.Destructive)
            ConfirmButton.Classes.Add("destructive");

        if (style != ConfirmStyle.Default)
        {
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            Opened += (_, _) => CancelButton.Focus();
        }
    }

    /// <summary>
    /// Aviso sem escolha — um relatório, um erro: um botão só, que o Return e o Esc acionam
    /// (a HIG não quer um Cancelar que faz o mesmo que o OK).
    /// </summary>
    public static ConfirmWindow Notice(string title, string headline, string body)
    {
        var window = new ConfirmWindow(title, headline, body, "Entendi");
        window.CancelButton.IsVisible = false;
        window.ConfirmButton.IsCancel = true;
        return window;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
