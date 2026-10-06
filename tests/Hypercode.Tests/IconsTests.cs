using System.Text.RegularExpressions;
using System.Xml.Linq;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Toda etiqueta aponta para um ícone que existe em Styles/Icons.axaml. A chave é só uma
/// string no view-model (para ele não depender do Avalonia), então um nome errado não
/// quebra o build: a etiqueta sairia sem desenho. E todo ícone do Icons.axaml vem do
/// gerador e aparece na galeria (#117).
/// </summary>
public class IconsTests
{
    [Fact]
    public void TodaEtiquetaTemIcone()
    {
        var icones = Icones();

        var semIcone = Enum.GetValues<BadgeKind>()
            .Select(kind => (kind, key: BadgeVisuals.IconKey(kind)))
            .Where(b => !icones.Contains(b.key))
            .Select(b => $"{b.kind} -> {b.key}");

        Assert.Empty(semIcone);
    }

    /// <summary>
    /// Ícone posto à mão no Icons.axaml, sem entrar em OCTICONS ou PHOSPHORS, some na próxima
    /// vez que alguém rodar o regenerar.sh.
    /// </summary>
    [Fact]
    public void TodoIconeEstaNoGerador()
    {
        var gerador = File.ReadAllText(Caminho("gen_icons.py"));
        var noGerador = Regex.Matches(gerador, @"\(""(Icon\.\w+)""").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Empty(Icones().Except(noGerador));
    }

    [Fact]
    public void TodoIconeApareceNaGaleria()
    {
        var galeria = File.ReadAllText(Caminho("ControlGallerySample.axaml"));
        var naGaleria = Regex.Matches(galeria, @"StaticResource (Icon\.\w+)").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Empty(Icones().Except(naGaleria));
    }

    private static HashSet<string> Icones()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Caminho("Icons.axaml")).Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .OfType<string>()
            .ToHashSet();
    }

    private static string Caminho(string arquivo) => Path.Combine(AppContext.BaseDirectory, arquivo);
}
