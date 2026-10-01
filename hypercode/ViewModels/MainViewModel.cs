using System.Collections.ObjectModel;
using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>
/// A janela: os repositórios abertos, um por aba, e qual está à frente. Cada aba é um
/// <see cref="RepositoryViewModel"/> vivo por conta própria — watcher, monitoramento e limpeza
/// seguem rodando com ela em segundo plano. Aqui ficam só abrir, fechar, reordenar e restaurar.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings;
    private readonly Action<Settings> _save;
    private readonly RepositoryHub _hub;
    private RepositoryViewModel? _selectedRepository;
    private bool _isWindowActive = true;
    private bool _isWindowMinimized;

    public MainViewModel()
        : this(SettingsStore.Load(), SettingsStore.Save, PullRequestMemory.Load(), AutoCleanupStore.Default)
    {
    }

    public MainViewModel(Settings settings, Action<Settings> save, PullRequestMemory pullRequests, AutoCleanupStore autoCleanupStore)
    {
        _settings = settings;
        _save = save;
        _hub = new RepositoryHub(settings, pullRequests, autoCleanupStore, SaveSettings, () => Repositories);
        Actions = new ActionsQueueViewModel(settings, SaveSettings);

        foreach (var path in settings.Repositories)
            Repositories.Add(new RepositoryViewModel(path, _hub));

        Select(Find(settings.SelectedRepository) ?? Repositories.FirstOrDefault(), persist: false);
    }

    /// <summary>As abas, na ordem da faixa.</summary>
    public ObservableCollection<RepositoryViewModel> Repositories { get; } = new();

    /// <summary>A aba à frente. A lista, o filtro e o rodapé da janela são os dela.</summary>
    public RepositoryViewModel? SelectedRepository
    {
        get => _selectedRepository;
        set => Select(value, persist: true);
    }

    /// <summary>A fila do GitHub Actions: o painel ao lado da lista e a janela própria mostram esta mesma.</summary>
    public ActionsQueueViewModel Actions { get; }

    public bool HasRepositories => Repositories.Count > 0;

    public bool HasNoRepositories => Repositories.Count == 0;

    public Settings Settings => _settings;

    /// <summary>Marca, no diálogo de criação, o checkbox de abrir o terminal no worktree novo. É global.</summary>
    public bool OpenTerminalAfterCreate
    {
        get => _settings.OpenTerminalAfterCreate;
        set
        {
            if (_settings.OpenTerminalAfterCreate == value) return;
            _settings.OpenTerminalAfterCreate = value;
            SaveSettings();
            RaisePropertyChanged();
        }
    }

    private void Select(RepositoryViewModel? repository, bool persist)
    {
        if (repository is not null && !Repositories.Contains(repository)) repository = null;
        if (ReferenceEquals(repository, _selectedRepository)) return;

        if (_selectedRepository is { } previous) previous.IsSelected = false;
        _selectedRepository = repository;
        if (repository is not null) repository.IsSelected = true;

        RaisePropertyChanged(nameof(SelectedRepository));

        // Só a aba da frente conta como janela ativa para o agendador: as outras seguem
        // monitoradas, na cadência de segundo plano.
        ReportActivity();

        if (!persist) return;
        _settings.SelectedRepository = repository?.RepositoryPath;
        SaveSettings();
    }

    /// <summary>⌘1…⌘9: a aba dessa posição, se existir.</summary>
    public void SelectIndex(int index)
    {
        if (index >= 0 && index < Repositories.Count) SelectedRepository = Repositories[index];
    }

    private RepositoryViewModel? Find(string? path)
        => path is null ? null : Repositories.FirstOrDefault(repository => PathsEqual(repository.RepositoryPath, path));

    /// <summary>
    /// Ao abrir a janela: resolve o repositório herdado da versão de uma aba só e carrega as
    /// abas, a da frente primeiro. Uma de cada vez: todas juntas disparariam o gh de uma vez só.
    /// </summary>
    public async Task OpenSavedAsync()
    {
        Actions.Start();

        if (_settings.LegacyRepository is { } legacy)
        {
            _settings.LegacyRepository = null;
            await MigrateLegacyAsync(legacy).ConfigureAwait(true);
        }

        var order = Repositories.ToList();
        if (SelectedRepository is { } selected)
        {
            order.Remove(selected);
            order.Insert(0, selected);
        }

        foreach (var repository in order)
            await repository.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// O caminho antigo era a raiz de qualquer worktree — o campo aceitava a pasta de um
    /// worktree secundário. A aba é do repositório dono dele.
    /// </summary>
    private async Task MigrateLegacyAsync(string legacy)
    {
        var (root, _) = await ResolveRepositoryAsync(ExpandHome(legacy)).ConfigureAwait(true);
        if (root is null || PathsEqual(root, legacy) || Find(legacy) is not { } old) return;

        var index = Repositories.IndexOf(old);
        var replacement = Find(root) ?? new RepositoryViewModel(root, _hub);

        old.Close();
        Repositories.RemoveAt(index);
        if (!Repositories.Contains(replacement)) Repositories.Insert(index, replacement);

        Select(replacement, persist: true);
        PersistRepositories();
    }

    /// <summary>
    /// Abre o repositório numa aba nova — pela aba +, pelo ⌘O ou arrastando a pasta. A pasta de
    /// um worktree abre o repositório dono dele; repositório já aberto só ganha o foco. Devolve
    /// o motivo, se não abriu.
    /// </summary>
    public async Task<string?> AddRepositoryAsync(string path)
    {
        path = ExpandHome(path.Trim());
        if (!Directory.Exists(path)) return $"A pasta {path} não existe.";

        var (root, error) = await ResolveRepositoryAsync(path).ConfigureAwait(true);
        if (root is null) return error;

        if (Find(root) is { } existing)
        {
            SelectedRepository = existing;
            return null;
        }

        var repository = new RepositoryViewModel(root, _hub);
        Repositories.Add(repository);
        PersistRepositories();
        SelectedRepository = repository;
        RaiseRepositoryCount();

        await repository.LoadAsync().ConfigureAwait(true);
        return null;
    }

    /// <summary>
    /// A raiz do worktree principal da pasta, pelo `git worktree list` — o primeiro da lista é
    /// sempre o principal, seja qual for o worktree de onde se pergunta.
    /// </summary>
    public static async Task<(string? Root, string? Error)> ResolveRepositoryAsync(string path)
    {
        try
        {
            var worktrees = await GitService.ListWorktreesAsync(path).ConfigureAwait(true);
            var main = worktrees.FirstOrDefault(worktree => worktree.IsMain)?.FullPath.TrimEnd('/');
            return main is { Length: > 0 }
                ? (main, null)
                : (null, $"{path} não é um repositório git.");
        }
        catch (GitNotFoundException exception)
        {
            return (null, exception.Message);
        }
        catch (Exception)
        {
            return (null, $"{path} não é um repositório git.");
        }
    }

    /// <summary>
    /// Fecha a aba: o repositório deixa de ser monitorado, e nada no disco muda. A carência da
    /// limpeza, a memória dos PRs e o override ficam guardados para quando ele voltar.
    /// </summary>
    public void CloseRepository(RepositoryViewModel repository)
    {
        var index = Repositories.IndexOf(repository);
        if (index < 0) return;

        // Antes de remover: a faixa de abas zera a seleção quando o item selecionado sai dela.
        var wasSelected = ReferenceEquals(_selectedRepository, repository);

        repository.Close();
        Repositories.RemoveAt(index);
        PersistRepositories();

        // A vizinha da direita toma o lugar; sem ela, a da esquerda.
        if (wasSelected || _selectedRepository is null)
            Select(Repositories.Count == 0 ? null : Repositories[Math.Min(index, Repositories.Count - 1)], persist: true);

        RaiseRepositoryCount();
    }

    /// <summary>Arrastar a aba: a ordem nova fica salva.</summary>
    public void MoveRepository(RepositoryViewModel repository, int newIndex)
    {
        var index = Repositories.IndexOf(repository);
        if (index < 0) return;

        newIndex = Math.Clamp(newIndex, 0, Repositories.Count - 1);
        if (newIndex == index) return;

        // A faixa de abas trata o Move como sair e voltar, e pode perder a seleção no caminho.
        var selected = _selectedRepository;
        Repositories.Move(index, newIndex);
        PersistRepositories();
        Select(selected, persist: true);
    }

    /// <summary>
    /// A janela avisa quando ganha ou perde o foco e quando é minimizada ou restaurada. Para o
    /// agendador, só a aba da frente está ativa; minimizada, nenhuma roda.
    /// </summary>
    public void SetWindowActivity(bool isActive, bool isMinimized)
    {
        _isWindowActive = isActive;
        _isWindowMinimized = isMinimized;
        ReportActivity();
        Actions.SetMainWindowActivity(isActive, isMinimized);
    }

    private void ReportActivity()
    {
        foreach (var repository in Repositories)
            repository.SetWindowActivity(_isWindowActive && repository.IsSelected, _isWindowMinimized);
    }

    /// <summary>Depois de a tela de configurações mudar algo: cada aba relê o que vale para ela.</summary>
    public void ApplySettingsChanged()
    {
        foreach (var repository in Repositories) repository.RefreshSettings();
        Actions.RefreshSettings();
    }

    public void SaveSettings() => _save(_settings);

    private void PersistRepositories()
    {
        _settings.Repositories = Repositories.Select(repository => repository.RepositoryPath).ToList();
        SaveSettings();
    }

    private void RaiseRepositoryCount()
    {
        RaisePropertyChanged(nameof(HasRepositories));
        RaisePropertyChanged(nameof(HasNoRepositories));
    }

    public static bool PathsEqual(string left, string? right)
        => right is not null
           && string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.Ordinal);

    public static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/", StringComparison.Ordinal)) return Path.Combine(home, path[2..]);
        return path;
    }
}
