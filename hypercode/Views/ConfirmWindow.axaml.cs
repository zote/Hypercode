using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hypercode.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string headline, string body, string confirmLabel) : this()
    {
        Title = title;
        SetText("HeadlineText", headline);
        SetText("BodyText", body);
        SetText("ConfirmButton", confirmLabel);
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
