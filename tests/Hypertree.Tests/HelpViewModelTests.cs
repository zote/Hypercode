using Hypertree.ViewModels;

namespace Hypertree.Tests;

public class HelpViewModelTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2.0-beta.1+8bd73dabed2446cea8b360c6090c67b2ef1a3dc3", "1.2.0-beta.1")]
    [InlineData("", "(versão desconhecida)")]
    [InlineData(null, "(versão desconhecida)")]
    public void DisplayVersion_TiraOHashDoCommit(string? informational, string expected)
    {
        Assert.Equal(expected, HelpViewModel.DisplayVersion(informational));
    }

    [Fact]
    public void VersionLabel_ComecaComONomeDoApp()
    {
        Assert.StartsWith("Hypertree ", new HelpViewModel().VersionLabel, StringComparison.Ordinal);
    }
}
