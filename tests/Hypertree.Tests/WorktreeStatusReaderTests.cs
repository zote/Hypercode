using Hypertree.Services;

namespace Hypertree.Tests;

public class WorktreeStatusReaderTests
{
    [Fact]
    public void Parse_BranchLimpaEmDia_SemIcones()
    {
        var status = WorktreeStatusReader.Parse(
            """
            # branch.oid 0123456789abcdef
            # branch.head main
            # branch.upstream origin/main
            # branch.ab +0 -0
            """,
            pendingOperation: null);

        Assert.True(status.IsKnown);
        Assert.False(status.HasUncommittedChanges);
        Assert.False(status.HasUnmergedPaths);
        Assert.True(status.HasUpstream);
        Assert.False(status.IsUpstreamGone);
        Assert.False(status.NeedsPush);
        Assert.False(status.NeedsPull);
        Assert.False(status.IsDiverged);
    }

    [Fact]
    public void Parse_AheadEBehind_Divergiu()
    {
        var status = WorktreeStatusReader.Parse(
            """
            # branch.head feature
            # branch.upstream origin/feature
            # branch.ab +3 -2
            """,
            pendingOperation: null);

        Assert.Equal(3, status.Ahead);
        Assert.Equal(2, status.Behind);
        Assert.True(status.IsDiverged);
        Assert.True(status.NeedsPush);
        Assert.True(status.NeedsPull);
    }

    [Fact]
    public void Parse_SemUpstream_NuncaFoiPushada()
    {
        var status = WorktreeStatusReader.Parse("# branch.head nova\n", pendingOperation: null);

        Assert.False(status.HasUpstream);
        Assert.False(status.IsUpstreamGone);
        Assert.True(status.NeedsPush);
    }

    [Fact]
    public void Parse_UpstreamSemContagem_UpstreamApagadoNoRemoto()
    {
        var status = WorktreeStatusReader.Parse(
            """
            # branch.head feature
            # branch.upstream origin/feature
            """,
            pendingOperation: null);

        Assert.True(status.HasUpstream);
        Assert.True(status.IsUpstreamGone);
        Assert.False(status.NeedsPush);
    }

    [Fact]
    public void Parse_LinhasDeMudanca_AlteracoesEConflito()
    {
        var status = WorktreeStatusReader.Parse(
            "# branch.head main\r\n"
            + "1 .M N... 100644 100644 100644 abc abc README.md\r\n"
            + "u UU N... 100644 100644 100644 100644 a b c conflito.cs\r\n",
            pendingOperation: "merge");

        Assert.True(status.HasUncommittedChanges);
        Assert.True(status.HasUnmergedPaths);
        Assert.Equal("merge", status.PendingOperation);
    }

    [Fact]
    public void Parse_SoArquivoNaoVersionado_ContaComoAlteracao()
    {
        var status = WorktreeStatusReader.Parse("# branch.head main\n? novo.txt\n", pendingOperation: null);

        Assert.True(status.HasUncommittedChanges);
        Assert.False(status.HasUnmergedPaths);
    }

    [Fact]
    public void Parse_ContagemMalFormada_Ignorada()
    {
        var status = WorktreeStatusReader.Parse(
            "# branch.upstream origin/x\n# branch.ab +x -\n",
            pendingOperation: null);

        Assert.Equal(0, status.Ahead);
        Assert.Equal(0, status.Behind);
        Assert.False(status.IsUpstreamGone);
    }

    [Fact]
    public void Unknown_NaoPedePush()
    {
        Assert.False(WorktreeStatus.Unknown.IsKnown);
        Assert.False(WorktreeStatus.Unknown.NeedsPush);
    }
}
