using Hypercode.Services;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// "Retomar a sessão do claude" acompanha a sessão que nasce depois de a lista carregar — num
/// terminal que o próprio app abriu —, sem esperar um recarregamento completo (#88).
/// </summary>
public sealed class ClaudeSessionTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();
    private readonly string? _previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

    public ClaudeSessionTests()
    {
        // As sessões vão para a sandbox, não para o ~/.claude do usuário.
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(_sandbox.Root, ".claude"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfigDir);
        _sandbox.Dispose();
    }

    [Fact]
    public async Task SessaoNovaHabilitaORetomarQuandoAJanelaVoltaParaAFrente()
    {
        _sandbox.CreateRepository("api", "feature");
        var worktree = Path.Combine(_sandbox.Root, "api-feature");
        var shell = _sandbox.OpenApp();
        Assert.Null(await shell.AddRepositoryAsync(worktree));

        var row = shell.SelectedRepository!.Worktrees.Single(item => item.FullPath == worktree);
        Assert.False(row.HasClaudeSession);

        // O claude rodado no terminal grava a conversa; o usuário volta para o app.
        shell.SetWindowActivity(isActive: false, isMinimized: false);
        CreateSession(worktree);
        shell.SetWindowActivity(isActive: true, isMinimized: false);

        await WaitUntilAsync(() => row.HasClaudeSession);
    }

    private void CreateSession(string directory)
    {
        var folder = Path.Combine(_sandbox.Root, ".claude", "projects", ClaudeSessions.EncodeProjectPath(directory));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{Guid.NewGuid()}.jsonl"), "{}\n");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "a condição não chegou a valer");
            await Task.Delay(20);
        }
    }
}
