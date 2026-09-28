using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hypercode.ViewModels;
using Hypercode.Views;

namespace Hypercode;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private AboutWindow? _aboutWindow;

    private void OnAboutClick(object? sender, EventArgs e)
    {
        // Um Sobre só: clicar de novo traz a janela aberta para a frente.
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;

        var owner = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is { IsVisible: true })
            _aboutWindow.Show(owner);
        else
            _aboutWindow.Show();
    }
}
