using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Confere as cores de Styles/Tokens.axaml lendo o XAML direto: as duas variantes têm as
/// mesmas chaves, todo token de texto passa em WCAG AA (4,5:1) e todo ícone de etiqueta em
/// 3:1 (1.4.11, contraste não textual) sobre os fundos em que pode aparecer.
/// </summary>
public class TokensTests
{
    private const double MinimoAA = 4.5;
    private const double MinimoNaoTextual = 3.0;

    private static readonly string[] Textos =
    [
        "Brush.Label.Primary", "Brush.Label.Secondary", "Brush.Text.Link",
        "Brush.Text.Danger", "Brush.Text.Warning", "Brush.Text.Success",
    ];

    private static readonly string[] Etiquetas =
    [
        "Brush.Badge.Success", "Brush.Badge.Danger", "Brush.Badge.Done",
        "Brush.Badge.Attention", "Brush.Badge.Accent", "Brush.Badge.Muted",
    ];

    private static readonly string[] Fundos =
    [
        "Brush.Background.Window", "Brush.Background.Content", "Brush.Background.ContentAlternate",
        "Brush.Background.Overlay",
    ];

    public static TheoryData<string> Variantes => new() { "Light", "Dark" };

    [Fact]
    public void VariantesTemAsMesmasChaves()
    {
        var tokens = Carregar();

        Assert.Equal(tokens["Light"].Keys.Order(), tokens["Dark"].Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Variantes))]
    public void TextoPassaEmAASobreOsFundos(string variante)
    {
        var cores = Carregar()[variante];
        var falhas =
            from texto in Textos
            from fundo in Fundos
            let razao = Contraste(cores[texto], cores[fundo])
            where razao < MinimoAA
            select $"{texto} sobre {fundo}: {razao:F2}";

        Assert.Empty(falhas);
    }

    // Brush.Selection.Inactive fica de fora: na linha selecionada a etiqueta não usa o
    // Brush.Badge.*, e sim a cor do texto da seleção (MainWindow.axaml, #108).
    [Theory]
    [MemberData(nameof(Variantes))]
    public void EtiquetaPassaEmContrasteNaoTextualSobreOsFundos(string variante)
    {
        var cores = Carregar()[variante];
        var falhas =
            from etiqueta in Etiquetas
            from fundo in Fundos
            let razao = Contraste(cores[etiqueta], cores[fundo])
            where razao < MinimoNaoTextual
            select $"{etiqueta} sobre {fundo}: {razao:F2}";

        Assert.Empty(falhas);
    }

    // Sobre Brush.Selection.Active vai Brush.Text.OnAccent, que não dá para medir aqui: o
    // fundo é a accent do sistema.
    [Theory]
    [MemberData(nameof(Variantes))]
    public void EtiquetaNaLinhaSelecionadaPassaEmContrasteNaoTextual(string variante)
    {
        var cores = Carregar()[variante];
        var razao = Contraste(cores["Brush.Label.Primary"], cores["Brush.Selection.Inactive"]);

        Assert.True(razao >= MinimoNaoTextual, $"Brush.Label.Primary sobre Brush.Selection.Inactive: {razao:F2}");
    }

    [Theory]
    [InlineData("Light", "Brush.Text.Danger", "Brush.Background.Danger")]
    [InlineData("Dark", "Brush.Text.Danger", "Brush.Background.Danger")]
    [InlineData("Light", "Brush.Text.Warning", "Brush.Background.Warning")]
    [InlineData("Dark", "Brush.Text.Warning", "Brush.Background.Warning")]
    public void TextoDeStatusPassaEmAANaCaixaDoStatus(string variante, string texto, string caixa)
    {
        var cores = Carregar()[variante];

        foreach (var fundo in Fundos)
        {
            var razao = Contraste(cores[texto], Compor(cores[caixa], cores[fundo]));
            Assert.True(razao >= MinimoAA, $"{texto} sobre {caixa} em {fundo}: {razao:F2}");
        }
    }

    private static Dictionary<string, Dictionary<string, Argb>> Carregar()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Tokens.axaml"));

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(x + "Key") is not null)
            .ToDictionary(
                d => d.Attribute(x + "Key")!.Value,
                d => d.Elements()
                    .Where(e => e.Name.LocalName == "SolidColorBrush")
                    .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => Argb.Parse(e.Attribute("Color")!.Value)));
    }

    /// <summary>Cor de frente com alfa composta sobre um fundo.</summary>
    private static Argb Compor(Argb frente, Argb fundo)
    {
        double Canal(byte f, byte b) => Math.Round(frente.A / 255.0 * f + (1 - frente.A / 255.0) * b);
        return new Argb(255, (byte)Canal(frente.R, fundo.R), (byte)Canal(frente.G, fundo.G), (byte)Canal(frente.B, fundo.B));
    }

    /// <summary>Razão de contraste WCAG 2.x, com o texto composto sobre o fundo.</summary>
    private static double Contraste(Argb texto, Argb fundo)
    {
        var claro = Luminancia(Compor(texto, fundo));
        var escuro = Luminancia(fundo);
        return (Math.Max(claro, escuro) + 0.05) / (Math.Min(claro, escuro) + 0.05);
    }

    private static double Luminancia(Argb c)
    {
        static double Linear(byte canal)
        {
            var v = canal / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private readonly record struct Argb(byte A, byte R, byte G, byte B)
    {
        public static Argb Parse(string hex)
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            var v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Argb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }
}
