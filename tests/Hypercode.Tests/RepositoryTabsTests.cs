using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>Abrir, fechar, reordenar e restaurar abas, com repositórios git de verdade.</summary>
public sealed class RepositoryTabsTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task PastaDeWorktreeAbreORepositorioDono()
    {
        var repository = _sandbox.CreateRepository("api", "feature");
        var shell = _sandbox.OpenApp();

        Assert.Null(await shell.AddRepositoryAsync(Path.Combine(_sandbox.Root, "api-feature")));

        var tab = Assert.Single(shell.Repositories);
        Assert.Equal(repository, tab.RepositoryPath);
        Assert.Same(tab, shell.SelectedRepository);
        Assert.Equal(2, tab.WorktreeCount);
        Assert.Equal(new[] { repository }, _sandbox.SavedSettings().Repositories);
    }

    [Fact]
    public async Task RepositorioRepetidoSoSelecionaAAbaExistente()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web");
        var shell = _sandbox.OpenApp();

        await shell.AddRepositoryAsync(api);
        await shell.AddRepositoryAsync(web);
        Assert.Null(await shell.AddRepositoryAsync(Path.Combine(_sandbox.Root, "api-feature")));

        Assert.Equal(new[] { api, web }, shell.Repositories.Select(tab => tab.RepositoryPath));
        Assert.Equal(api, shell.SelectedRepository?.RepositoryPath);
    }

    [Fact]
    public async Task PastaQueNaoERepositorioNaoCriaAba()
    {
        var shell = _sandbox.OpenApp();

        var notGit = await shell.AddRepositoryAsync(_sandbox.CreateFolder("fotos"));
        var missing = await shell.AddRepositoryAsync(Path.Combine(_sandbox.Root, "nao-existe"));

        Assert.Contains("não é um repositório git", notGit);
        Assert.Contains("não existe", missing);
        Assert.Empty(shell.Repositories);
        Assert.True(shell.HasNoRepositories);
    }

    [Fact]
    public async Task FecharAAbaSelecionaAVizinhaENaoTocaNoDisco()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var web = _sandbox.CreateRepository("web");
        var cli = _sandbox.CreateRepository("cli");
        var shell = _sandbox.OpenApp();
        foreach (var path in new[] { api, web, cli }) await shell.AddRepositoryAsync(path);

        shell.SelectedRepository = shell.Repositories[1];
        shell.CloseRepository(shell.Repositories[1]);

        Assert.Equal(new[] { api, cli }, shell.Repositories.Select(tab => tab.RepositoryPath));
        Assert.Equal(cli, shell.SelectedRepository?.RepositoryPath);
        Assert.True(Directory.Exists(Path.Combine(web, ".git")));
        Assert.Equal(new[] { api, cli }, _sandbox.SavedSettings().Repositories);

        shell.CloseRepository(shell.Repositories[1]);
        shell.CloseRepository(shell.Repositories[0]);

        Assert.Null(shell.SelectedRepository);
        Assert.True(shell.HasNoRepositories);
        Assert.Null(_sandbox.SavedSettings().SelectedRepository);
        Assert.True(Directory.Exists(Path.Combine(_sandbox.Root, "api-feature")));
    }

    [Fact]
    public async Task ReabrirOAppRestauraAbasOrdemESelecao()
    {
        var api = _sandbox.CreateRepository("api");
        var web = _sandbox.CreateRepository("web");
        var cli = _sandbox.CreateRepository("cli");
        var shell = _sandbox.OpenApp();
        foreach (var path in new[] { api, web, cli }) await shell.AddRepositoryAsync(path);

        shell.MoveRepository(shell.Repositories[2], 0);
        shell.SelectedRepository = shell.Repositories[1];

        var reopened = _sandbox.OpenApp();
        await reopened.OpenSavedAsync();

        Assert.Equal(new[] { cli, api, web }, reopened.Repositories.Select(tab => tab.RepositoryPath));
        Assert.Equal(api, reopened.SelectedRepository?.RepositoryPath);
        Assert.All(reopened.Repositories, tab => Assert.Equal(1, tab.WorktreeCount));
    }

    [Fact]
    public async Task ConfiguracaoAntigaAbreORepositorioComoPrimeiraAba()
    {
        var api = _sandbox.CreateRepository("api", "feature");
        var worktree = Path.Combine(_sandbox.Root, "api-feature");

        // O campo antigo aceitava a pasta de um worktree qualquer.
        File.WriteAllText(_sandbox.SettingsFile, $$"""{ "RepositoryPath": "{{worktree}}", "MonitorProfile": "off" }""");

        var shell = _sandbox.OpenApp();
        await shell.OpenSavedAsync();

        var tab = Assert.Single(shell.Repositories);
        Assert.Equal(api, tab.RepositoryPath);
        Assert.Same(tab, shell.SelectedRepository);
        Assert.Equal(2, tab.WorktreeCount);

        var saved = _sandbox.SavedSettings();
        Assert.Equal(new[] { api }, saved.Repositories);
        Assert.Equal(api, saved.SelectedRepository);
        Assert.DoesNotContain("RepositoryPath", File.ReadAllText(_sandbox.SettingsFile));
    }

    [Fact]
    public async Task SinoAcendeSoNaAbaDeTrasEApagaAoSelecionar()
    {
        var shell = _sandbox.OpenApp();
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("api"));
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("web"));
        var (api, web) = (shell.Repositories[0], shell.Repositories[1]);

        web.NoteUnseenChanges();
        api.NoteUnseenChanges();

        Assert.False(web.HasUnseenChanges);
        Assert.True(api.HasUnseenChanges);
        Assert.Contains("PR mudou", api.TabToolTip);

        shell.SelectedRepository = api;

        Assert.False(api.HasUnseenChanges);
    }

    [Fact]
    public async Task OrdenacaoEDeCadaRepositorio()
    {
        var shell = _sandbox.OpenApp();
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("api"));
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("web"));

        shell.Repositories[0].SortBy(SortColumn.PullRequest);

        var saved = _sandbox.SavedSettings();
        Assert.Equal("pullrequest", saved.OverridesFor(shell.Repositories[0].RepositoryPath)?.SortColumn);
        Assert.Null(saved.OverridesFor(shell.Repositories[1].RepositoryPath));
        Assert.Equal("name", saved.SortColumn);
    }

    [Fact]
    public async Task OverrideDaTelaValeSoParaORepositorioDele()
    {
        var shell = _sandbox.OpenApp();
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("api"));
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("web"));
        var (api, web) = (shell.Repositories[0], shell.Repositories[1]);

        var settings = new SettingsViewModel(shell, api);
        Assert.True(settings.IsRepositoryScope);
        Assert.True(settings.AutoCleanupUsesGlobal);
        Assert.False(settings.CanEditAutoCleanup);

        // Desmarcar "usar o global" começa pelo valor global; depois o campo é do repositório.
        settings.AutoCleanupUsesGlobal = false;
        Assert.True(settings.IsAutoCleanupOverridden);
        Assert.False(settings.AutoCleanup);

        settings.AutoCleanup = true;
        settings.AutoCleanupGraceMinutes = 3;
        settings.Command = "claude --resume";

        Assert.True(api.AutoCleanup);
        Assert.Equal(3, api.Effective.AutoCleanupGraceMinutes);
        Assert.False(web.AutoCleanup);
        Assert.False(shell.Settings.AutoCleanup);

        // O comando foi escrito com o campo travado no global: não vale, e o global fica intacto.
        Assert.Equal("claude", api.EffectiveCommand);
        Assert.Equal("claude", shell.Settings.Command);

        // No global, a mudança vale para quem não sobrescreveu.
        settings.SelectedScope = settings.Scopes[0];
        settings.Command = "zsh";
        Assert.Equal("zsh", api.EffectiveCommand);
        Assert.Equal("zsh", web.EffectiveCommand);
        Assert.True(api.AutoCleanup);

        // Voltar a usar o global tira o override do arquivo.
        settings.SelectedScope = settings.Scopes.Single(scope => ReferenceEquals(scope.Repository, api));
        settings.AutoCleanupUsesGlobal = true;
        Assert.False(api.AutoCleanup);
        Assert.Null(_sandbox.SavedSettings().OverridesFor(api.RepositoryPath)?.AutoCleanup);

        settings.Detach();
    }

    [Fact]
    public async Task FecharORepositorioDoEscopoVoltaAoGlobal()
    {
        var shell = _sandbox.OpenApp();
        await shell.AddRepositoryAsync(_sandbox.CreateRepository("api"));
        var settings = new SettingsViewModel(shell, shell.Repositories[0]);

        shell.CloseRepository(shell.Repositories[0]);

        Assert.False(settings.IsRepositoryScope);
        Assert.Single(settings.Scopes);
        settings.Detach();
    }
}
