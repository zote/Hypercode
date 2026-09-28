using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Hypercode.Views;

public partial class ControlGalleryWindow : Window
{
    public ControlGalleryWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Só um controle tem o foco por vez: a lista clara mostra a seleção ativa e a escura,
        // a inativa ("escuro" inverte). "foco" põe o foco de teclado num botão, para mostrar
        // o anel; "tooltip" abre os tooltips de amostra.
        var args = Environment.GetCommandLineArgs();
        var focused = args.Contains("escuro") ? "DarkSample" : "LightSample";
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var sample = this.FindControl<ControlGallerySample>(focused);
            if (args.Contains("foco"))
                sample?.FocusButtonByKeyboard();
            else
                sample?.FocusTable();
            if (!args.Contains("tooltip")) return;
            this.FindControl<ControlGallerySample>("LightSample")?.OpenToolTip();
            this.FindControl<ControlGallerySample>("DarkSample")?.OpenToolTip();
        }, DispatcherPriority.Loaded);
    }
}
