using Avalonia.Controls;
using Avalonia.Interactivity;
using Hypercode.ViewModels;

namespace Hypercode.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        DataContext = new HelpViewModel();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
