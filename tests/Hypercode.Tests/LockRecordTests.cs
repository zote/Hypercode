using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public class LockRecordTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    // 1787771509 = 26/08/2026 19:11:49 UTC = 16:11 em São Paulo.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void RegistroDoSupacodeViraDonoEData()
    {
        var lines = LockRecord.Describe(
            """{"build":"1785775286","createdAt":1787771509,"owner":"supacode","version":"0.10.8"}""",
            Now,
            SaoPaulo);

        Assert.Equal(
            new[]
            {
                ("dono", "supacode 0.10.8 (build 1785775286)"),
                ("travado", "26/08/2026 16:11 (há 32 dias)"),
            },
            lines);
    }

    [Fact]
    public void CampoDesconhecidoApareceComoVeio()
    {
        var lines = LockRecord.Describe(
            """{"owner":"supacode","createdAt":1787771509,"session":"abc","pid":42}""",
            Now,
            SaoPaulo);

        Assert.Equal(
            new[]
            {
                ("dono", "supacode"),
                ("travado", "26/08/2026 16:11 (há 32 dias)"),
                ("session", "abc"),
                ("pid", "42"),
            },
            lines);
    }

    [Fact]
    public void OutroCampoDeInstanteTambemViraData()
    {
        var lines = LockRecord.Describe("""{"owner":"x","updatedAt":1787771509000}""", Now, SaoPaulo);

        Assert.Contains(("updatedAt", "26/08/2026 16:11 (há 32 dias)"), lines!);
    }

    [Fact]
    public void RegistroEscapadoPeloGitAindaEhLido()
    {
        var lines = LockRecord.Describe("""
            "{\"owner\":\"supacode\",\"createdAt\":1787771509}"
            """, Now, SaoPaulo);

        Assert.Equal(new[] { ("dono", "supacode"), ("travado", "26/08/2026 16:11 (há 32 dias)") }, lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não mexa, estou usando")]
    [InlineData("""{"owner":"supacode",""")]
    [InlineData("[1,2]")]
    public void RegistroQueNaoEhObjetoJsonDevolveNull(string? reason)
        => Assert.Null(LockRecord.Describe(reason, Now, SaoPaulo));

    [Theory]
    [InlineData(30, "agora há pouco")]
    [InlineData(60, "há 1 minuto")]
    [InlineData(90 * 60, "há 1 hora")]
    [InlineData(5 * 3600, "há 5 horas")]
    [InlineData(86400, "há 1 dia")]
    public void TempoRelativo(int secondsAgo, string expected)
    {
        var created = Now.AddSeconds(-secondsAgo).ToUnixTimeSeconds();
        var lines = LockRecord.Describe($$"""{"createdAt":{{created}}}""", Now, SaoPaulo);

        Assert.EndsWith($"({expected})", lines!.Single().Value);
    }

    [Fact]
    public void InstanteNoFuturoNaoGanhaTempoRelativo()
    {
        var created = Now.AddDays(1).ToUnixTimeSeconds();
        var lines = LockRecord.Describe($$"""{"createdAt":{{created}}}""", Now, SaoPaulo);

        Assert.Equal("29/09/2026 12:00", lines!.Single().Value);
    }
}
