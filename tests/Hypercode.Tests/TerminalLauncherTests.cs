using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

public sealed class TerminalLauncherTests
{
    [Fact]
    public void ComandoLimpaAsVariaveisDeSessaoHerdadasAntesDoCd()
    {
        var shell = TerminalLauncher.BuildShellCommand("/Users/voce/dev/repo", "claude");

        Assert.Equal(
            "unset CLAUDE_CODE_CHILD_SESSION CLAUDE_CODE_ENTRYPOINT; if cd '/Users/voce/dev/repo'; then claude; "
            + "else echo 'Hypercode: o terminal não conseguiu entrar em /Users/voce/dev/repo.'; fi",
            shell);
    }

    [Fact]
    public void SemComandoAindaLimpaAsVariaveisParaQuemRodarClaudeADepois()
    {
        var shell = TerminalLauncher.BuildShellCommand("/Users/voce/dev/repo", "  ");

        Assert.Equal(
            "unset CLAUDE_CODE_CHILD_SESSION CLAUDE_CODE_ENTRYPOINT; cd '/Users/voce/dev/repo' "
            + "|| echo 'Hypercode: o terminal não conseguiu entrar em /Users/voce/dev/repo.'",
            shell);
    }

    [Fact]
    public void OUnsetChegaAosDoisScriptsDeAppleScript()
    {
        var shell = TerminalLauncher.BuildShellCommand("/tmp/x", "claude");

        Assert.Contains("unset CLAUDE_CODE_CHILD_SESSION", TerminalLauncher.BuildITerm2Script(shell));
        Assert.Contains("unset CLAUDE_CODE_CHILD_SESSION", TerminalLauncher.BuildTerminalAppScript(shell));
    }

    [Fact]
    public void CdQueFalhaEmVolumeExternoApontaAPermissaoDoTerminal()
    {
        var shell = TerminalLauncher.BuildShellCommand("/Volumes/Mac/dev/repo", "claude", "iTerm2");

        Assert.Contains("o iTerm2 não conseguiu entrar em /Volumes/Mac/dev/repo", shell);
        Assert.Contains("Arquivos e Pastas → iTerm2 → Volumes removíveis", shell);
    }

    [Fact]
    public void RecusaDeAutomacaoDizOndeAutorizar()
    {
        var message = TerminalLauncher.DescribeFailure(
            TerminalLauncher.ITerm2,
            "execution error: Not authorized to send Apple events to iTerm2. (-1743)");

        Assert.Contains("não tem permissão para controlá-lo", message);
        Assert.Contains("Privacidade e Segurança → Automação", message);
    }

    private static Func<TerminalDefinition, bool> Installed(params TerminalDefinition[] terminals)
        => terminal => terminals.Contains(terminal);

    [Fact]
    public void PadraoDoSistemaEhOITerm2SeHouver()
    {
        Assert.Equal(TerminalLauncher.ITerm2, TerminalLauncher.Resolve(null, Installed(TerminalLauncher.ITerm2, TerminalLauncher.AppleTerminal)).Definition);
        Assert.Equal(TerminalLauncher.AppleTerminal, TerminalLauncher.Resolve("system", Installed(TerminalLauncher.AppleTerminal)).Definition);
    }

    [Fact]
    public void EscolhaInstaladaVale()
    {
        var choice = TerminalLauncher.Resolve("ghostty", Installed(TerminalLauncher.ITerm2, TerminalLauncher.Ghostty));

        Assert.Equal(TerminalLauncher.Ghostty, choice.Definition);
        Assert.False(choice.IsSystemDefault);
        Assert.Null(choice.Notice);
    }

    [Theory]
    [InlineData("kitty", "O kitty escolhido")]
    [InlineData("hyper", "O hyper escolhido")]
    public void EscolhaQueSumiuVoltaAoPadraoComAviso(string preference, string expected)
    {
        var choice = TerminalLauncher.Resolve(preference, Installed(TerminalLauncher.AppleTerminal));

        Assert.Equal(TerminalLauncher.AppleTerminal, choice.Definition);
        Assert.True(choice.IsSystemDefault);
        Assert.StartsWith(expected, choice.Notice);
        Assert.EndsWith("o padrão do sistema, o Terminal.", choice.Notice);
    }

    [Fact]
    public void GhosttyAbrePeloOpenComShellQueSobraDepoisDoComando()
    {
        var arguments = TerminalLauncher.BuildOpenArguments(
            TerminalLauncher.Ghostty, "/Applications/Ghostty.app", "/dev/repo", "cd '/dev/repo'", "repo · main", "/bin/zsh");

        Assert.Equal(
            new[]
            {
                "-n", "-a", "/Applications/Ghostty.app", "--args",
                "--working-directory=/dev/repo", "--title=repo · main",
                "-e", "/bin/zsh", "-lic", "cd '/dev/repo'; exec '/bin/zsh' -l",
            },
            arguments);
    }

    [Theory]
    [InlineData("wezterm", "start --cwd /dev/repo -- /bin/zsh -lic")]
    [InlineData("alacritty", "--working-directory /dev/repo --title repo -e /bin/zsh -lic")]
    [InlineData("kitty", "--directory /dev/repo --title repo /bin/zsh -lic")]
    public void TerminaisSemAppleScriptRecebemAPastaEOShell(string id, string expected)
    {
        var terminal = TerminalLauncher.Find(id)!;
        var arguments = TerminalLauncher.BuildOpenArguments(terminal, $"/Applications/{terminal.Bundles[0]}", "/dev/repo", "claude", "repo", "/bin/zsh");

        Assert.Equal(new[] { "-n", "-a", $"/Applications/{terminal.Bundles[0]}", "--args" }, arguments.Take(4));
        Assert.Equal(expected, string.Join(' ', arguments.Skip(4).SkipLast(1)));
        Assert.Equal("claude; exec '/bin/zsh' -l", arguments[^1]);
    }

    [Fact]
    public void SoITerm2ETerminalSabemDarFoco()
        => Assert.Equal(
            new[] { "iterm2", "terminal" },
            TerminalLauncher.Known.Where(terminal => terminal.CanFocus).Select(terminal => terminal.Id));
}
