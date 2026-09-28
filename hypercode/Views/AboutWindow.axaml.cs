using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hypercode.Views;

public partial class AboutWindow : Window
{
    private static readonly Uri RepositoryUri = new("https://github.com/zote/Hypercode");

    public AboutWindow()
    {
        InitializeComponent();
        this.FindControl<TextBlock>("VersionText")!.Text = $"Versão {AppVersion()}";
    }

    /// <summary>
    /// A &lt;Version&gt; do .csproj. O SDK anexa "+&lt;commit&gt;" à versão informacional;
    /// esse sufixo não interessa a quem lê o Sobre.
    /// </summary>
    private static string AppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "?";
    }

    private async void OnRepositoryClick(object? sender, RoutedEventArgs e)
        => await Launcher.LaunchUriAsync(RepositoryUri);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // Esc por KeyDown, não por IsCancel/IsDefault no botão: com eles, trazer a janela
    // já aberta para a frente pelo menu (Activate) às vezes a fechava sozinha.
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
