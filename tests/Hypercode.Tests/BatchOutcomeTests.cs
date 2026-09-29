using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

// HasProblems decide se o lote abre o relatório (#95): sucesso fica no rodapé, e o diálogo
// só aparece quando há algo que falhou ou ficou de fora.
public sealed class BatchOutcomeTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void TudoCertoNaoTemProblema()
    {
        var outcome = new BatchOutcome("Puxar do remoto", new[] { "a", "b" }, None, None);

        Assert.False(outcome.HasProblems);
        Assert.Equal("Puxar do remoto: 2 ok", outcome.Summary);
    }

    [Fact]
    public void FalhaTemProblema()
        => Assert.True(new BatchOutcome("Puxar do remoto", new[] { "a" }, new[] { "b: recusado" }, None).HasProblems);

    // Com a condição antiga da trava (Failed.Count > 0), este caso não dava explicação nenhuma.
    [Fact]
    public void SoNaoAplicavelTemProblema()
        => Assert.True(new BatchOutcome("Travar os worktrees", None, None, new[] { "principal" }).HasProblems);
}
