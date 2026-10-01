using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void RepositorioUnicoAntigoViraPrimeiraAba()
    {
        var settings = SettingsStore.Parse("""{ "RepositoryPath": "/Users/voce/dev/repo", "Command": "zsh" }""");

        Assert.Equal(new[] { "/Users/voce/dev/repo" }, settings.Repositories);
        Assert.Equal("/Users/voce/dev/repo", settings.SelectedRepository);
        Assert.Equal("/Users/voce/dev/repo", settings.LegacyRepository);
        Assert.Null(settings.RepositoryPath);
        Assert.Equal("zsh", settings.Command);

        // A chave antiga some do arquivo no primeiro salvamento; a marca de migração nunca vai para ele.
        var saved = SettingsStore.Serialize(settings);
        Assert.DoesNotContain("RepositoryPath", saved);
        Assert.DoesNotContain("LegacyRepository", saved);
    }

    [Fact]
    public void ListaDeAbasPrevaleceSobreAChaveAntiga()
    {
        var settings = SettingsStore.Parse("""
            { "RepositoryPath": "/antigo", "Repositories": ["/a", "/b", "/a", ""], "SelectedRepository": "/b" }
            """);

        Assert.Equal(new[] { "/a", "/b" }, settings.Repositories);
        Assert.Equal("/b", settings.SelectedRepository);
        Assert.Null(settings.LegacyRepository);
    }

    [Fact]
    public void ColecoesNulasNoArquivoNaoDerrubamOApp()
    {
        var settings = SettingsStore.Parse("""{ "Repositories": null, "RepositoryOverrides": null }""");

        Assert.Empty(settings.Repositories);
        Assert.Empty(settings.RepositoryOverrides);
    }

    [Fact]
    public void OverrideValeCampoACampoEORestoSegueOGlobal()
    {
        var settings = new Settings
        {
            Command = "claude",
            MonitorProfile = "economical",
            NotifyPullRequestChanges = true,
            AssignIssueOnCreate = false,
            AutoCleanup = false,
            AutoCleanupGraceMinutes = 10,
        };
        var overrides = settings.EnsureOverrides("/repo");
        overrides.AutoCleanup = true;
        overrides.AutoCleanupGraceMinutes = 3;
        overrides.Command = "claude --resume";

        var repository = EffectiveSettings.Resolve(settings, "/repo");
        Assert.Equal("claude --resume", repository.Command);
        Assert.True(repository.AutoCleanup);
        Assert.Equal(3, repository.AutoCleanupGraceMinutes);
        Assert.Equal(MonitorProfile.Economical, repository.MonitorProfile);
        Assert.True(repository.NotifyPullRequestChanges);
        Assert.False(repository.AssignIssueOnCreate);

        // Outro repositório, e o global puro, não veem o override.
        foreach (var other in new[] { EffectiveSettings.Resolve(settings, "/outro"), EffectiveSettings.Resolve(settings, null) })
        {
            Assert.Equal("claude", other.Command);
            Assert.False(other.AutoCleanup);
            Assert.Equal(10, other.AutoCleanupGraceMinutes);
        }
    }

    [Fact]
    public void ValoresInvalidosVoltamAoPadrao()
    {
        var settings = new Settings { Command = "  ", MonitorProfile = "turbo", AutoCleanupGraceMinutes = 0 };

        var effective = EffectiveSettings.Resolve(settings, null);

        Assert.Equal("claude", effective.Command);
        Assert.Equal(MonitorProfile.Balanced, effective.MonitorProfile);
        Assert.Equal(AutoCleanupTracker.DefaultGraceMinutes, effective.AutoCleanupGraceMinutes);
    }

    [Fact]
    public void OverrideVazioSaiDoArquivo()
    {
        var settings = new Settings();
        settings.EnsureOverrides("/repo").Command = "zsh";
        settings.PruneOverrides("/repo");
        Assert.NotNull(settings.OverridesFor("/repo"));

        settings.EnsureOverrides("/repo").Command = null;
        settings.PruneOverrides("/repo");
        Assert.Null(settings.OverridesFor("/repo"));
    }

    [Fact]
    public void OverrideSobreviveAIdaEVoltaPeloArquivo()
    {
        var settings = new Settings { Repositories = { "/repo" } };
        var overrides = settings.EnsureOverrides("/repo");
        overrides.AutoCleanup = true;
        overrides.SortColumn = "pullrequest";

        var json = SettingsStore.Serialize(settings);
        var reloaded = SettingsStore.Parse(json);

        Assert.True(reloaded.OverridesFor("/repo")?.AutoCleanup);
        Assert.Equal("pullrequest", reloaded.OverridesFor("/repo")?.SortColumn);
        Assert.Null(reloaded.OverridesFor("/repo")?.Command);

        // Campo sem override não vai para o arquivo como null: fica claro o que o repositório sobrescreve.
        Assert.DoesNotContain("\"Command\": null", json);
    }

    [Fact]
    public void TerminalPadraoNaoVaiParaOArquivoEOEscolhidoVolta()
    {
        Assert.DoesNotContain("\"Terminal\"", SettingsStore.Serialize(new Settings()));

        var reloaded = SettingsStore.Parse(SettingsStore.Serialize(new Settings { Terminal = "ghostty" }));
        Assert.Equal("ghostty", reloaded.Terminal);
    }

    [Fact]
    public void SeletorDeTerminalMostraSoOsInstaladosEAvisaQuemNaoDaFoco()
    {
        var options = SettingsViewModel.BuildTerminalOptions(new HashSet<string> { "terminal", "kitty", "iterm2" });

        Assert.Equal(new string?[] { null, "iterm2", "terminal", "kitty" }, options.Select(option => option.Id));
        Assert.Equal("Padrão do sistema (iTerm2)", options[0].Label);
        Assert.Equal("iTerm2", options[1].Label);
        Assert.Equal("kitty — sem \"Ir para o terminal aberto\"", options[3].Label);
    }
}
