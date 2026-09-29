using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public class TitleBarDoubleClickTests
{
    [Theory]
    [InlineData("Maximize", TitleBarDoubleClickAction.Zoom)]
    [InlineData("Fill", TitleBarDoubleClickAction.Zoom)]
    [InlineData(null, TitleBarDoubleClickAction.Zoom)]
    [InlineData("Minimize", TitleBarDoubleClickAction.Minimize)]
    [InlineData("None", TitleBarDoubleClickAction.None)]
    public void PreferenciaDoSistemaViraAcao(string? value, TitleBarDoubleClickAction expected)
        => Assert.Equal(expected, TitleBarDoubleClick.Parse(value));

    [Fact]
    public void LerAPreferenciaDoSistemaNaoFalha()
        => Assert.True(Enum.IsDefined(TitleBarDoubleClick.Current()));
}

public class TitleBarInsetTests
{
    [Theory]
    [InlineData(true, Avalonia.Controls.WindowState.Normal, true)]
    [InlineData(true, Avalonia.Controls.WindowState.Maximized, true)]
    [InlineData(true, Avalonia.Controls.WindowState.FullScreen, false)]
    [InlineData(false, Avalonia.Controls.WindowState.Normal, false)]
    public void AbasRecuamParaOsBotoesDeJanelaSoQuandoElesAparecem(bool extended, Avalonia.Controls.WindowState state, bool inset)
        => Assert.Equal(inset, Hypercode.Views.MainWindow.NeedsTitleBarInset(extended, state));
}
