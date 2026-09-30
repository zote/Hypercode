using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public sealed class TerminalCadenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static MonitorScheduler Scanned(bool isActive = true)
    {
        var scheduler = new MonitorScheduler { IsWindowActive = isActive };
        scheduler.MarkTerminalsScanned(Start);
        return scheduler;
    }

    [Fact]
    public void NuncaLidoVenceAVarredura()
        => Assert.Equal(TerminalCheck.Scan, new MonitorScheduler().TerminalCheckDue(Start));

    [Fact]
    public void JanelaNaFrenteSondaA3sEVarreA15s()
    {
        var scheduler = Scanned();

        Assert.Equal(TerminalCheck.None, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(2)));
        Assert.Equal(TerminalCheck.Probe, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(3)));

        scheduler.MarkTerminalsProbed(Start + TimeSpan.FromSeconds(3));
        Assert.Equal(TerminalCheck.None, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(5)));
        Assert.Equal(TerminalCheck.Scan, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void JanelaEmSegundoPlanoTriplica()
    {
        var scheduler = Scanned(isActive: false);

        Assert.Equal(TerminalCheck.None, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(8)));
        Assert.Equal(TerminalCheck.Probe, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(9)));
        Assert.Equal(TerminalCheck.Scan, scheduler.TerminalCheckDue(Start + TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void JanelaMinimizadaNaoLeNada()
    {
        var scheduler = new MonitorScheduler { IsWindowMinimized = true };

        Assert.Equal(TerminalCheck.None, scheduler.TerminalCheckDue(Start + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void PerfilDesligadoNaoCalaOsTerminais()
    {
        // O perfil e a cota são do GitHub; a leitura dos terminais é local.
        var scheduler = new MonitorScheduler { Profile = MonitorProfile.Off };

        Assert.Equal(TerminalCheck.Scan, scheduler.TerminalCheckDue(Start));
    }
}
