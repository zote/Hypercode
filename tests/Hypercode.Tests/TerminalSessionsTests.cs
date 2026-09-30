using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

public sealed class TerminalSessionsTests
{
    // A árvore real do iTerm2: iTerm2 → iTermServer → login → zsh → claude.
    private static readonly List<ProcessEntry> Processes = TerminalSessions.ParsePs("""
          100     1 ??       1-02:00:00 /Applications/iTerm.app/Contents/MacOS/iTerm2
          110   100 ??          59:00 /Users/voce/Library/Application Support/iTerm2/iTermServer-3.7.3
          200   110 ttys001     30:00 /usr/bin/login
          201   200 ttys001     30:00 -zsh
          202   201 ttys001     29:00 claude
          203     1 ttys001     29:59 -zsh
          300   110 ttys002     05:00 /usr/bin/login
          301   300 ttys002     05:00 -zsh
          400     1 ??        1:00:00 /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal
          401   400 ttys003     10:00 /usr/bin/login
          402   401 ttys003     10:00 -zsh
          500     1 ??          02:00 dotnet watch
          600     1 ttys009     01:00 /Applications/Ghostty.app/Contents/MacOS/zsh
          900     1 ??          01:00 /Applications/Hypercode.app/Contents/MacOS/Hypercode
          901   900 ??          00:00 /usr/sbin/lsof
        """);

    private static readonly string[] Worktrees = { "/dev/repo", "/dev/repo/.claude/worktrees/issue-1", "/dev/outro" };

    private static Dictionary<string, TerminalPresence> Assign(params (int Pid, string Directory)[] directories)
        => TerminalSessions.Assign(Worktrees, directories, Processes, selfPid: 900);

    [Fact]
    public void LeOLsofComPidEPasta()
    {
        var entries = TerminalSessions.ParseLsof("p180\nfcwd\nn/\np202\nfcwd\nn/dev/repo com espaço\n");

        Assert.Equal(new[] { (180, "/"), (202, "/dev/repo com espaço") }, entries);
    }

    [Theory]
    [InlineData("05:07", 0, 0, 5, 7)]
    [InlineData("1:02:03", 0, 1, 2, 3)]
    [InlineData("2-01:02:03", 2, 1, 2, 3)]
    public void LeOEtimeDoPs(string value, int days, int hours, int minutes, int seconds)
        => Assert.Equal(new TimeSpan(days, hours, minutes, seconds), TerminalSessions.ParseElapsed(value));

    [Fact]
    public void LeOPsComTtyEComandoComEspaco()
    {
        Assert.Null(Processes.Single(process => process.Pid == 500).Tty);
        Assert.Equal("dotnet watch", Processes.Single(process => process.Pid == 500).Command);
        Assert.Equal("/dev/ttys001", Processes.Single(process => process.Pid == 202).Tty);
    }

    [Fact]
    public void DescobreOAppPelaAscendencia()
    {
        var presence = Assign((202, "/dev/repo"), (402, "/dev/outro"), (600, "/dev/outro/src"));

        Assert.Equal(TerminalApp.ITerm2, presence["/dev/repo"].Sessions.Single().App);
        Assert.Equal(
            new[] { TerminalApp.Other, TerminalApp.Terminal },
            presence["/dev/outro"].Sessions.Select(session => session.App));
    }

    [Fact]
    public void ShellOrfaoHerdaOAppDoTty()
    {
        // O zsh 203 tem o pai morto, mas o tty dele é do iTerm2.
        var session = Assign((203, "/dev/repo")).Values.Single().Sessions.Single();

        Assert.Equal(TerminalApp.ITerm2, session.App);
        Assert.Equal(TimeSpan.FromMinutes(30), session.Age);
    }

    [Fact]
    public void MaisRecenteVemPrimeiroEEhADoFoco()
    {
        var presence = Assign((202, "/dev/repo"), (301, "/dev/repo/src"), (500, "/dev/repo"))["/dev/repo"];

        Assert.Equal(new[] { "/dev/ttys002", "/dev/ttys001" }, presence.Sessions.Select(session => session.Tty));
        Assert.Equal("/dev/ttys002", presence.Focusable?.Tty);
        // Só o que roda fora das sessões: o claude e o zsh já estão nelas.
        Assert.Equal(new[] { "dotnet" }, presence.Processes);
    }

    [Fact]
    public void WorktreeAninhadoFicaComOTerminalDeDentro()
    {
        var presence = Assign((202, "/dev/repo/.claude/worktrees/issue-1/backend"));

        Assert.Equal(new[] { "/dev/repo/.claude/worktrees/issue-1" }, presence.Keys);
    }

    [Fact]
    public void PrefixoDeNomeNaoEhSubpasta()
        => Assert.Empty(Assign((202, "/dev/repo-outro")));

    [Fact]
    public void OProprioAppEOsFilhosDeleFicamDeFora()
        => Assert.Empty(Assign((900, "/dev/repo"), (901, "/dev/repo")));

    [Fact]
    public void ProcessoSemTerminalAcendeSemSessaoParaFoco()
    {
        var presence = Assign((500, "/dev/repo"))["/dev/repo"];

        Assert.Empty(presence.Sessions);
        Assert.Null(presence.Focusable);
        Assert.False(presence.IsEmpty);
    }

    [Fact]
    public void TerminalDesconhecidoNaoRecebeFoco()
        => Assert.Null(Assign((600, "/dev/outro"))["/dev/outro"].Focusable);

    [Fact]
    public void LeOsTtysVivosDoPs()
        => Assert.Equal(
            new HashSet<string> { "/dev/ttys001", "/dev/ttys003" },
            TerminalSessions.ParseTtys("??\n  ttys001\nttys001\n??\nttys003 \n-\n"));

    [Fact]
    public async Task SondagemLeOPsDeVerdade()
    {
        // Sempre há processo sem terminal (o launchd, no mínimo): null aqui seria o ps falhando.
        var live = await TerminalSessions.ListLiveTtysAsync();

        Assert.NotNull(live);
        Assert.All(live!, tty => Assert.StartsWith("/dev/", tty));
    }

    [Fact]
    public void TtySumidoApagaASessao()
    {
        var presence = Assign((202, "/dev/repo"), (301, "/dev/repo/src"), (500, "/dev/repo"))["/dev/repo"];

        var pruned = TerminalSessions.Prune(presence, new HashSet<string> { "/dev/ttys001" });

        Assert.Equal(new[] { "/dev/ttys001" }, pruned.Sessions.Select(session => session.Tty));
        // O ps de tty não diz nada de quem não tem terminal: o dotnet fica.
        Assert.Equal(new[] { "dotnet" }, pruned.Processes);
    }

    [Fact]
    public void UltimaSessaoFechadaApagaOIcone()
    {
        var presence = Assign((202, "/dev/repo"))["/dev/repo"];

        Assert.True(TerminalSessions.Prune(presence, new HashSet<string> { "/dev/ttys009" }).IsEmpty);
    }

    [Fact]
    public void TodoTtyVivoDevolveAMesmaPresenca()
    {
        var presence = Assign((202, "/dev/repo"))["/dev/repo"];

        Assert.Same(presence, TerminalSessions.Prune(presence, new HashSet<string> { "/dev/ttys001", "/dev/ttys002" }));
    }

    [Fact]
    public void ANovaLeituraHerdaONomeDaMesmaSessao()
    {
        var before = new TerminalSession("/dev/ttys001", TerminalApp.ITerm2, TimeSpan.FromMinutes(29), "✳ Resolver (claude)");

        var carried = TerminalSessions.CarryNames(Assign((202, "/dev/repo")), new[] { before }, out var hasNew);

        Assert.Equal("✳ Resolver (claude)", carried["/dev/repo"].Sessions.Single().Name);
        Assert.False(hasNew);
    }

    [Fact]
    public void SessaoNovaDoITerm2PedeONome()
    {
        var carried = TerminalSessions.CarryNames(Assign((202, "/dev/repo")), Array.Empty<TerminalSession>(), out var hasNew);

        Assert.Null(carried["/dev/repo"].Sessions.Single().Name);
        Assert.True(hasNew);
    }

    [Fact]
    public void TtyReaproveitadoPorSessaoNovaNaoHerdaONome()
    {
        // A sessão de antes já tinha uma hora; a do ttys001 de agora tem 30 min: é outra.
        var before = new TerminalSession("/dev/ttys001", TerminalApp.ITerm2, TimeSpan.FromHours(1), "velha");

        var carried = TerminalSessions.CarryNames(Assign((202, "/dev/repo")), new[] { before }, out var hasNew);

        Assert.Null(carried["/dev/repo"].Sessions.Single().Name);
        Assert.True(hasNew);
    }

    [Fact]
    public void SessaoNovaForaDoITerm2NaoPedeNome()
    {
        TerminalSessions.CarryNames(Assign((402, "/dev/outro")), Array.Empty<TerminalSession>(), out var hasNew);

        Assert.False(hasNew);
    }

    [Fact]
    public void LeOsNomesDasSessoesDoITerm2()
    {
        var names = TerminalSessions.ParseSessionNames("/dev/ttys003\t✳ Resolver bagunça da branch (claude)\n/dev/ttys004\t\nlixo\n");

        Assert.Equal(new Dictionary<string, string> { ["/dev/ttys003"] = "✳ Resolver bagunça da branch (claude)" }, names);
    }

    [Fact]
    public void ScriptsDeFocoProcuramOTty()
    {
        Assert.Contains("if tty of s is \"/dev/ttys002\" then", TerminalSessions.BuildITerm2FocusScript("/dev/ttys002"));
        Assert.Contains("if tty of t is \"/dev/ttys002\" then", TerminalSessions.BuildTerminalAppFocusScript("/dev/ttys002"));
    }

    [Fact]
    public void PresencaIgualNaoRedesenhaALinha()
    {
        var first = Assign((202, "/dev/repo"))["/dev/repo"];
        var second = Assign((202, "/dev/repo"))["/dev/repo"];

        Assert.Equal(first, second);
    }

    [Fact]
    public void LinhaComTerminalGanhaOIconeComONomeNoTooltip()
    {
        var row = new WorktreeRow(new WorktreeInfo { FullPath = "/dev/repo", Branch = "main" })
        {
            Terminal = new TerminalPresence(
                new[] { new TerminalSession("/dev/ttys003", TerminalApp.ITerm2, TimeSpan.FromMinutes(1), "✳ Resolver bagunça (claude)") },
                new[] { "dotnet" }),
        };

        var badge = row.GitBadges.First();
        Assert.Equal(BadgeKind.TerminalOpen, badge.Kind);
        Assert.Contains("iTerm2 — ✳ Resolver bagunça (claude)", badge.Tooltip);
        Assert.Contains("Duplo-clique vai para ele", badge.Tooltip);
        Assert.Contains("Fora do terminal, com a pasta aberta: dotnet.", badge.Tooltip);
        Assert.True(row.CanFocusTerminal);
    }

    [Fact]
    public void LinhaSoComProcessoGanhaOIconeApagadoESemFoco()
    {
        var row = new WorktreeRow(new WorktreeInfo { FullPath = "/dev/repo", Branch = "main" })
        {
            Terminal = new TerminalPresence(Array.Empty<TerminalSession>(), new[] { "dotnet" }),
        };

        Assert.Equal(BadgeKind.ProcessOpen, row.GitBadges.First().Kind);
        Assert.False(row.CanFocusTerminal);
    }
}
