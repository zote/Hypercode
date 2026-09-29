using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Anel de foco do campo de texto (#97): o campo em foco mostra o anel externo no clique e no
/// Tab, como o NSTextField, em vez de trocar a borda.
/// </summary>
public sealed class FocusRingTests
{
    [AvaloniaTheory]
    [InlineData(NavigationMethod.Pointer)]
    [InlineData(NavigationMethod.Tab)]
    public void CampoEmFocoMostraOAnelEOTiraAoPerderOFoco(NavigationMethod metodo)
    {
        var campo = new TextBox();
        var botao = new Button { Content = "Outro" };
        var janela = new Window { Content = new StackPanel { Children = { campo, botao } } };
        janela.Show();

        campo.Focus(metodo);
        Dispatcher.UIThread.RunJobs();
        var anel = Assert.IsType<Border>(AdornerLayer.GetAdorner(campo));
        Assert.Equal(new Thickness(3), anel.BorderThickness);
        Assert.Single(AdornerLayer.GetAdornerLayer(campo)!.Children);

        botao.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(AdornerLayer.GetAdorner(campo));

        janela.Close();
    }
}
