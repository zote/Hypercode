using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

public sealed class WorktreeLayoutTests
{
    [Fact]
    public void SemConfiguracaoARaizContinuaAoLadoDoRepositorio()
    {
        Assert.Equal("/dev/locanota.worktrees", WorktreeCreator.WorktreesRoot("/dev/locanota/"));
        Assert.Equal("/dev/locanota.worktrees/feat-476-anexo", WorktreeCreator.SuggestPath("/dev/locanota", "feat/476-anexo"));
    }

    [Theory]
    [InlineData("/Volumes/Mac/dev/locanota-worktrees", "/Volumes/Mac/dev/locanota-worktrees")]
    [InlineData(".claude/worktrees", "/dev/locanota/.claude/worktrees")]
    [InlineData("../{repo}-worktrees/", "/dev/locanota-worktrees")]
    [InlineData("  ", "/dev/locanota.worktrees")]
    public void RaizConfiguradaAbsolutaRelativaOuComNomeDoRepositorio(string configured, string expected)
        => Assert.Equal(expected, WorktreeCreator.WorktreesRoot("/dev/locanota", configured));

    [Fact]
    public void RaizComTilVaiParaAHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "wt", "locanota"), WorktreeCreator.WorktreesRoot("/dev/locanota", "~/wt/{repo}"));
    }

    [Fact]
    public void BarrasPreservadasViramSubpasta()
        => Assert.Equal(
            "/dev/locanota-worktrees/feat/476-anexo",
            WorktreeCreator.SuggestPath("/dev/locanota", "feat/476-anexo", "../locanota-worktrees", keepSlashes: true));

    [Fact]
    public void InfereRaizEBarrasPeloCaminho()
    {
        Assert.Equal(new WorktreeLayoutGuess("/x/wt", true), WorktreeCreator.InferLayout("/x/wt/feat/login", "feat/login"));
        Assert.Equal(new WorktreeLayoutGuess("/x/wt", false), WorktreeCreator.InferLayout("/x/wt/feat-login/", "feat/login"));

        // Branch sem barra não diz nada das barras; caminho que não termina na branch não diz nada.
        Assert.Equal(new WorktreeLayoutGuess("/x/wt", null), WorktreeCreator.InferLayout("/x/wt/login", "login"));
        Assert.Null(WorktreeCreator.InferLayout("/x/wt/outra-coisa", "feat/login"));
    }

    [Fact]
    public void DetectaARaizDoLocanotaComPrefixoEmSubpasta()
    {
        var worktrees = new[]
        {
            Worktree("/dev/locanota", "main", isMain: true),
            Worktree("/dev/locanota-worktrees/feat/476-anexo", "feat/476-anexo"),
            Worktree("/dev/locanota-worktrees/feat/480-nota", "feat/480-nota"),
            Worktree("/dev/locanota-worktrees/chore/deps", "chore/deps"),
            Worktree("/dev/locanota-worktrees/ci/cache", "ci/cache"),
            Worktree("/dev/locanota.worktrees/docs-readme", "docs/readme"),
        };

        Assert.Equal(new WorktreeLayoutGuess("/dev/locanota-worktrees", true), WorktreeCreator.DetectLayout(worktrees));
    }

    [Fact]
    public void DetectaARaizDentroDoRepositorio()
    {
        var worktrees = new[]
        {
            Worktree("/dev/hypercode", "main", isMain: true),
            Worktree("/dev/hypercode/.claude/worktrees/issue-12-lock", "claude/issue-12-lock"),
            Worktree("/dev/hypercode/.claude/worktrees/issue-13-tabs", "claude/issue-13-tabs"),
        };

        // Empate entre .claude/worktrees e .claude (o avô): fica com a mais funda. A pasta não repete
        // a branch (o AGENTS.md tira o claude/), então as barras ficam sem voto.
        Assert.Equal(new WorktreeLayoutGuess("/dev/hypercode/.claude/worktrees", null), WorktreeCreator.DetectLayout(worktrees));
    }

    [Fact]
    public void SemMaioriaOuComUmWorktreeSoNaoDetecta()
    {
        Assert.Null(WorktreeCreator.DetectLayout(new[] { Worktree("/a/wt/x", "x") }));
        Assert.Null(WorktreeCreator.DetectLayout(new[] { Worktree("/a/wt/x", "x"), Worktree("/b/outra/y", "y") }));
    }

    [Fact]
    public void RaizGravadaRelativaQuandoFicaDentroDoRepositorio()
    {
        Assert.Equal(".claude/worktrees", WorktreeCreator.RootSetting("/dev/hypercode", "/dev/hypercode/.claude/worktrees"));
        Assert.Equal("/dev/hypercode.worktrees", WorktreeCreator.RootSetting("/dev/hypercode", "/dev/hypercode.worktrees"));
    }

    [Fact]
    public void RaizEBarrasValemPorRepositorioSobreOGlobal()
    {
        var settings = new Settings { WorktreesRoot = "../{repo}-worktrees", WorktreeFolderKeepsSlashes = true };
        settings.EnsureOverrides("/hypercode").WorktreesRoot = ".claude/worktrees";
        settings.EnsureOverrides("/hypercode").WorktreeFolderKeepsSlashes = false;
        settings.EnsureOverrides("/padrao").WorktreesRoot = "";

        var hypercode = EffectiveSettings.Resolve(settings, "/hypercode");
        Assert.Equal(".claude/worktrees", hypercode.WorktreesRoot);
        Assert.False(hypercode.WorktreeFolderKeepsSlashes);

        var locanota = EffectiveSettings.Resolve(settings, "/locanota");
        Assert.Equal("../{repo}-worktrees", locanota.WorktreesRoot);
        Assert.True(locanota.WorktreeFolderKeepsSlashes);

        // Vazio no override é o padrão explícito, mesmo com o global configurado.
        Assert.Null(EffectiveSettings.Resolve(settings, "/padrao").WorktreesRoot);

        var reloaded = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.Equal(".claude/worktrees", reloaded.OverridesFor("/hypercode")?.WorktreesRoot);
        Assert.False(reloaded.OverridesFor("/hypercode")?.WorktreeFolderKeepsSlashes);
    }

    [Fact]
    public void DialogoSugereOndeOsWorktreesJaMoramEGravaNoRepositorio()
    {
        var existing = new[]
        {
            Worktree("/dev/locanota", "main", isMain: true),
            Worktree("/dev/locanota-worktrees/feat/1-a", "feat/1-a"),
            Worktree("/dev/locanota-worktrees/chore/b", "chore/b"),
        };
        (string Root, bool? KeepSlashes)? remembered = null;

        var dialog = new CreateWorktreeViewModel(
            "/dev/locanota", openTerminal: false, command: "claude", assignIssue: false,
            worktreesRoot: null, keepSlashes: false, existing, (root, slashes) => remembered = (root, slashes))
        {
            IsNewBranchMode = true,
            BranchName = "feat/2-nota",
        };

        Assert.Equal("/dev/locanota-worktrees/feat/2-nota", dialog.WorktreePath);
        Assert.NotNull(dialog.LayoutHint);
        Assert.True(dialog.CanRememberLayout);

        dialog.RememberLayout();

        Assert.Equal(("/dev/locanota-worktrees", (bool?)true), remembered);
        Assert.False(dialog.CanRememberLayout);
    }

    [Fact]
    public void DialogoComRaizConfiguradaIgnoraADeteccao()
    {
        var existing = new[] { Worktree("/dev/r-wt/a", "a"), Worktree("/dev/r-wt/b", "b") };

        var dialog = new CreateWorktreeViewModel(
            "/dev/r", openTerminal: false, command: "claude", assignIssue: false,
            worktreesRoot: ".wt", keepSlashes: false, existing, (_, _) => { })
        {
            IsNewBranchMode = true,
            BranchName = "feat/x",
        };

        Assert.Equal("/dev/r/.wt/feat-x", dialog.WorktreePath);
        Assert.Null(dialog.LayoutHint);
        Assert.False(dialog.CanRememberLayout);

        // Editar a pasta para outra raiz oferece gravá-la.
        dialog.WorktreePath = "/dev/outra/feat/x";
        Assert.True(dialog.CanRememberLayout);
    }

    [Fact]
    public async Task PastaDentroDoRepositorioSoEhIgnoradaComRegra()
    {
        using var sandbox = new GitSandbox();
        var repository = sandbox.CreateRepository("repo");
        var inside = Path.Combine(repository, ".claude", "worktrees", "feat-x");

        Assert.True(WorktreeCreator.IsInsideRepository(repository, inside));
        Assert.False(WorktreeCreator.IsInsideRepository(repository, repository + ".worktrees/feat-x"));
        Assert.False(await WorktreeCreator.IsIgnoredAsync(repository, inside));

        await File.AppendAllTextAsync(Path.Combine(repository, ".git", "info", "exclude"), ".claude/worktrees/\n");
        Assert.True(await WorktreeCreator.IsIgnoredAsync(repository, inside));
    }

    private static WorktreeInfo Worktree(string path, string branch, bool isMain = false)
        => new() { FullPath = path, Branch = branch, IsMain = isMain };
}
