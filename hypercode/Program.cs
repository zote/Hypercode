using Avalonia;
using Avalonia.Platform;

namespace Hypercode;

internal static class Program
{
    // Avalonia precisa rodar na thread principal em STA.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // OpenGL é o primeiro da ordem padrão do macOS e tem corrida conhecida na
            // destruição da sessão de render (AvaloniaUI/Avalonia#21519, SIGSEGV em
            // libAvaloniaNative). Isto desvia do código defeituoso, não corrige a causa:
            // Metal primeiro, software como rede.
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = new[]
                {
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.Software,
                },
            })
            .WithInterFont()
            .LogToTrace();
}
