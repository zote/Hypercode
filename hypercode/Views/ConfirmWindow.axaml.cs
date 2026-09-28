using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hypercode.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    /// <param name="cancelIsDefault">
    /// Enter cancela em vez de confirmar — para ações em que o engano custa mais que o clique extra.
    /// </param>
    public ConfirmWindow(string title, string headline, string body, string confirmLabel, bool cancelIsDefault = false) : this()
    {
        Title = title;
        SetText("HeadlineText", headline);
        SetText("BodyText", body);
        SetText("ConfirmButton", confirmLabel);

        if (cancelIsDefault)
        {
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            Opened += (_, _) => CancelButton.Focus();
        }
    }

    private void SetText(string controlName, string value)
    {
        switch (this.FindControl<Control>(controlName))
        {
            case TextBlock textBlock: textBlock.Text = value; break;
            case ContentControl contentControl: contentControl.Content = value; break;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
