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
            "unset CLAUDE_CODE_CHILD_SESSION CLAUDE_CODE_ENTRYPOINT; cd '/Users/voce/dev/repo' && claude",
            shell);
    }

    [Fact]
    public void SemComandoAindaLimpaAsVariaveisParaQuemRodarClaudeADepois()
    {
        var shell = TerminalLauncher.BuildShellCommand("/Users/voce/dev/repo", "  ");

        Assert.Equal(
            "unset CLAUDE_CODE_CHILD_SESSION CLAUDE_CODE_ENTRYPOINT; cd '/Users/voce/dev/repo'",
            shell);
    }

    [Fact]
    public void OUnsetChegaAosDoisScriptsDeAppleScript()
    {
        var shell = TerminalLauncher.BuildShellCommand("/tmp/x", "claude");

        Assert.Contains("unset CLAUDE_CODE_CHILD_SESSION", TerminalLauncher.BuildITerm2Script(shell));
        Assert.Contains("unset CLAUDE_CODE_CHILD_SESSION", TerminalLauncher.BuildTerminalAppScript(shell));
    }
}
