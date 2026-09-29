using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hypercode.ViewModels;
using Hypercode.Views;

namespace Hypercode;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Janela fora de foco ganha a classe "inactive": Styles/Controls.axaml a usa para o
        // botão padrão perder o destaque, como no macOS. O Avalonia não tem pseudo-classe
        // para isso (a seleção de lista não precisa: a janela inativa já perde o
        // :focus-within).
        Window.IsActiveProperty.Changed.AddClassHandler<Window>(
            (window, _) => window.Classes.Set("inactive", !window.IsActive));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --galeria abre só a amostra do design system (Styles/README.md), sem repositório.
            desktop.MainWindow = desktop.Args?.Contains("--galeria") == true
                ? new ControlGalleryWindow()
                : new MainWindow { DataContext = new MainViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private AboutWindow? _aboutWindow;

    private void OnSettingsClick(object? sender, EventArgs e)
        => ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.ShowSettings();

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
