using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// O botão de destaque pressionado escurece em relação ao solto em todas as accents do macOS
/// (#135), inclusive na vermelha, em que o SystemAccentColorDark2 do Fluent sai igual ao Dark1.
/// </summary>
public sealed class AccentPressedTests
{
    // Mínimo de contraste entre solto e pressionado. O Dark2 do Fluent dá de 1,35 a 1,54:1 nas
    // accents em que escurece direito; no vermelho dava 1,16:1 (#FF1920 → #EF0007) e no rosa
    // 1,26:1. Com o escurecimento em HSV, o menor é o roxo, 1,34:1.
    private const double MinimoEntreSoltoEPressionado = 1.3;

    // O SystemAccentColorDark1 que o Fluent 11.3 deriva de cada accent do macOS: é o fundo do
    // botão solto (Brush.Selection.Active).
    public static TheoryData<string, string> Soltos => new()
    {
        { "vermelho", "#FF1920" },
        { "laranja", "#D26607" },
        { "amarelo", "#C69A00" },
        { "verde", "#4C9136" },
        { "azul", "#005FC6" },
        { "roxo", "#7F3E80" },
        { "rosa", "#F51880" },
        { "grafite", "#707070" },
    };

    [Theory]
    [MemberData(nameof(Soltos))]
    public void PressionadoEscureceEmTodaAccent(string accent, string hex)
    {
        var solto = Color.Parse(hex);
        var pressionado = AccentPressed.Escurecer(solto);

        var razao = Contraste(solto, pressionado);
        Assert.True(razao >= MinimoEntreSoltoEPressionado, $"{accent}: {solto} → {pressionado}, {razao:F2}:1");
    }

    [Fact]
    public void PressionadoMantemMatizESaturacao()
    {
        var solto = Color.Parse("#7F3E80").ToHsv();
        var pressionado = AccentPressed.Escurecer(Color.Parse("#7F3E80")).ToHsv();

        Assert.Equal(solto.H, pressionado.H, 0);
        Assert.True(Math.Abs(solto.S - pressionado.S) < 0.01, $"saturação {solto.S:F3} → {pressionado.S:F3}");
        Assert.True(pressionado.V < solto.V);
    }

    [AvaloniaFact]
    public void PressionadoAcompanhaATrocaDaAccent()
    {
        var solto = new SolidColorBrush(Color.Parse("#005FC6"));
        var pressionado = new SolidColorBrush(Colors.Transparent);
        var dono = new Border
        {
            Resources = { ["Brush.Selection.Active"] = solto, ["Brush.Accent.Pressed"] = pressionado },
        };

        AccentPressed.Acompanhar(dono);
        Assert.Equal(AccentPressed.Escurecer(Color.Parse("#005FC6")), pressionado.Color);

        solto.Color = Color.Parse("#FF1920");
        Assert.Equal(AccentPressed.Escurecer(Color.Parse("#FF1920")), pressionado.Color);
    }

    [AvaloniaFact]
    public void AppDerivaOPressionadoDoSolto()
    {
        var janela = new Window { Content = new Button { Classes = { "accent" }, Content = "Padrão" } };
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        var app = Application.Current!;
        Assert.True(app.TryGetResource("Brush.Selection.Active", null, out var s));
        Assert.True(app.TryGetResource("Brush.Accent.Pressed", null, out var p));
        Assert.Equal(AccentPressed.Escurecer(((SolidColorBrush)s!).Color), ((SolidColorBrush)p!).Color);

        janela.Close();
    }

    /// <summary>Razão de contraste WCAG 2.x entre duas cores opacas.</summary>
    private static double Contraste(Color a, Color b)
    {
        var (la, lb) = (Luminancia(a), Luminancia(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminancia(Color c)
    {
        static double Linear(byte canal)
        {
            var v = canal / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
