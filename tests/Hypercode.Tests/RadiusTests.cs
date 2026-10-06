using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Hypercode.Tests;

/// <summary>Os raios que dependem uns dos outros para as curvas ficarem concêntricas (#97).</summary>
public sealed class RadiusTests
{
    [AvaloniaFact]
    public void RaiosAcompanhamACurvaDoControleQueEnvolvem()
    {
        CornerRadius Raio(string chave) => (CornerRadius)Application.Current!.FindResource(chave)!;
        var recuoDoItem = ((Thickness)Application.Current!.FindResource("Margin.MenuItem")!).Left;

        // O anel fica 3 por fora do controle; o destaque do item, 5 por dentro do menu.
        Assert.Equal(Raio("Radius.Control").TopLeft + 3, Raio("Radius.FocusRing").TopLeft);
        Assert.Equal(Raio("Radius.Group").TopLeft - recuoDoItem, Raio("Radius.MenuItem").TopLeft);
    }
}
