using System.Xml.Linq;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Toda etiqueta aponta para um ícone que existe em Styles/Icons.axaml. A chave é só uma
/// string no view-model (para ele não depender do Avalonia), então um nome errado não
/// quebra o build: a etiqueta sairia sem desenho.
/// </summary>
public class IconsTests
{
    [Fact]
    public void TodaEtiquetaTemIcone()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var icones = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Icons.axaml")).Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .OfType<string>()
            .ToHashSet();

        var semIcone = Enum.GetValues<BadgeKind>()
            .Select(kind => (kind, key: BadgeVisuals.IconKey(kind)))
            .Where(b => !icones.Contains(b.key))
            .Select(b => $"{b.kind} -> {b.key}");

        Assert.Empty(semIcone);
    }
}
