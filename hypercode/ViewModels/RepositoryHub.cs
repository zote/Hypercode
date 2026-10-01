using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>
/// O que os repositórios abertos dividem entre si. As preferências e a memória dos PRs são um
/// arquivo só cada: uma cópia por aba faria uma gravação apagar a outra. A vez de falar com o
/// remoto e a cota do GitHub também são de todos.
/// </summary>
public sealed class RepositoryHub
{
    private readonly Action _saveSettings;
    private readonly Func<IEnumerable<RepositoryViewModel>> _repositories;

    public RepositoryHub(
        Settings settings,
        PullRequestMemory pullRequests,
        AutoCleanupStore autoCleanupStore,
        Action saveSettings,
        Func<IEnumerable<RepositoryViewModel>> repositories)
    {
        Settings = settings;
        PullRequests = pullRequests;
        AutoCleanupStore = autoCleanupStore;
        _saveSettings = saveSettings;
        _repositories = repositories;
    }

    public Settings Settings { get; }

    public PullRequestMemory PullRequests { get; }

    /// <summary>A carência e o histórico da limpeza de todos os repositórios, num arquivo só, chaveado pela raiz.</summary>
    public AutoCleanupStore AutoCleanupStore { get; }

    /// <summary>Um repositório de cada vez no fetch e na consulta dos PRs do monitoramento.</summary>
    public SemaphoreSlim RemoteGate { get; } = new(1, 1);

    public void SaveSettings() => _saveSettings();

    /// <summary>A cota GraphQL é da conta: o que um repositório leu vale para os outros.</summary>
    public void ShareBudget(RepositoryViewModel source, ApiBudget? budget)
    {
        if (budget is null) return;

        foreach (var repository in _repositories())
            if (!ReferenceEquals(repository, source)) repository.ObserveBudget(budget);
    }
}
