using System.Collections.ObjectModel;
using Hypertree.Services;

namespace Hypertree.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings;
    private CancellationTokenSource? _loadCancellation;

    private string _repositoryPath = string.Empty;
    private string _command = "claude";
    private string _statusMessage = "Escolha um repositório git para listar os worktrees.";
    private bool _isBusy;
    private WorktreeRow? _selectedWorktree;
    private string _filterText = string.Empty;
    private SortColumn _sortColumn;
    private bool _sortDescending;

    public MainViewModel()
    {
        _settings = SettingsStore.Load();
        _repositoryPath = _settings.RepositoryPath ?? string.Empty;
        _command = string.IsNullOrWhiteSpace(_settings.Command) ? "claude" : _settings.Command;
        _sortColumn = ParseSortColumn(_settings.SortColumn);
        _sortDescending = _settings.SortDescending;
    }

    /// <summary>Todos os worktrees do repositório, na ordem do git. É a base da limpeza.</summary>
    public ObservableCollection<WorktreeRow> Worktrees { get; } = new();

    /// <summary>O que a lista mostra: <see cref="Worktrees"/> filtrado e ordenado.</summary>
    public ObservableCollection<WorktreeRow> VisibleWorktrees { get; } = new();

    public bool OpenTerminalAfterCreate
    {
        get => _settings.OpenTerminalAfterCreate;
        set
        {
            if (_settings.OpenTerminalAfterCreate == value) return;
            _settings.OpenTerminalAfterCreate = value;
            PersistSettings();
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Texto do filtro. Cada palavra precisa aparecer no nome, na branch, no número
    /// ou no título do PR — "login 412" acha o worktree da branch login com PR #412.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty)) ApplyView();
        }
    }

    public bool HasHiddenRows => VisibleWorktrees.Count < Worktrees.Count;

    public string FilterSummary => HasHiddenRows
        ? $"{VisibleWorktrees.Count} de {Worktrees.Count}"
        : string.Empty;

    public string NameHeader => HeaderLabel("WORKTREE", SortColumn.Name);
    public string BranchHeader => HeaderLabel("BRANCH", SortColumn.Branch);
    public string PullRequestHeader => HeaderLabel("PR", SortColumn.PullRequest);
    public string PathHeader => HeaderLabel("CAMINHO", SortColumn.Path);

    private string HeaderLabel(string label, SortColumn column)
        => column != _sortColumn ? label : label + (_sortDescending ? " ▼" : " ▲");

    /// <summary>Clique no cabeçalho: ordena pela coluna; clicar de novo na mesma inverte.</summary>
    public void SortBy(SortColumn column)
    {
        if (column == _sortColumn) _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        _settings.SortColumn = column.ToString().ToLowerInvariant();
        _settings.SortDescending = _sortDescending;
        PersistSettings();

        RaisePropertyChanged(nameof(NameHeader));
        RaisePropertyChanged(nameof(BranchHeader));
        RaisePropertyChanged(nameof(PullRequestHeader));
        RaisePropertyChanged(nameof(PathHeader));
        ApplyView();
    }

    /// <summary>
    /// Refaz <see cref="VisibleWorktrees"/>. O principal fica sempre no topo, seja qual for
    /// a ordenação; linhas sem PR vão para o fim quando a ordenação é por PR.
    /// </summary>
    private void ApplyView()
    {
        var terms = _filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var rows = Worktrees.Where(row => terms.All(row.Matches));

        var comparer = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<WorktreeRow> ordered = rows.OrderBy(row => row.Worktree.IsMain ? 0 : 1);

        ordered = _sortColumn switch
        {
            SortColumn.Branch => ThenByText(ordered, row => row.Branch, comparer),
            SortColumn.PullRequest => ordered
                .ThenBy(row => row.PullRequest is null ? 1 : 0)
                .Then(row => row.PullRequest?.Number ?? 0, _sortDescending),
            SortColumn.Path => ThenByText(ordered, row => row.FullPath, comparer),
            _ => ThenByText(ordered, row => row.Name, comparer),
        };

        var result = ordered.ToList();

        if (!result.SequenceEqual(VisibleWorktrees))
        {
            var selected = SelectedWorktree;

            VisibleWorktrees.Clear();
            foreach (var row in result) VisibleWorktrees.Add(row);

            SelectedWorktree = selected is not null && result.Contains(selected)
                ? selected
                : result.FirstOrDefault();
        }

        RaisePropertyChanged(nameof(HasHiddenRows));
        RaisePropertyChanged(nameof(FilterSummary));
    }

    private IOrderedEnumerable<WorktreeRow> ThenByText(
        IOrderedEnumerable<WorktreeRow> source,
        Func<WorktreeRow, string> key,
        IComparer<string> comparer)
        => _sortDescending ? source.ThenByDescending(key, comparer) : source.ThenBy(key, comparer);

    private static SortColumn ParseSortColumn(string? value)
        => Enum.TryParse<SortColumn>(value, ignoreCase: true, out var column) ? column : SortColumn.Name;

    public string RepositoryPath
    {
        get => _repositoryPath;
        set => SetProperty(ref _repositoryPath, value);
    }

    public string Command
    {
        get => _command;
        set
        {
            if (SetProperty(ref _command, value)) PersistSettings();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value)) RaisePropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !_isBusy;

    public WorktreeRow? SelectedWorktree
    {
        get => _selectedWorktree;
        set => SetProperty(ref _selectedWorktree, value);
    }

    public bool HasStartedOnce { get; private set; }

    public int CompletedCount => Worktrees.Count(row => row.IsCompleted);

    public bool HasCompleted => CompletedCount > 0;

    public string CleanupButtonLabel =>
        CompletedCount > 0 ? $"Limpar concluídos ({CompletedCount})" : "Limpar concluídos";

    private void RaiseCleanupState()
    {
        RaisePropertyChanged(nameof(CompletedCount));
        RaisePropertyChanged(nameof(HasCompleted));
        RaisePropertyChanged(nameof(CleanupButtonLabel));
    }

    public IReadOnlyList<WorktreeRow> CleanupCandidates()
        => Worktrees.Where(row => row.IsCompleted).ToList();

    /// <summary>Texto listado no diálogo de confirmação.</summary>
    public static string BuildCleanupSummary(IReadOnlyList<WorktreeRow> rows)
        => string.Join(
            "\n",
            rows.Select(row =>
                $"{row.Name}  —  {row.Branch}\n"
                + $"    motivo: {row.CompletionReason}\n"
                + $"    {row.FullPath}\n"));

    /// <summary>
    /// Remove os worktrees concluídos. Os órfãos não são removidos um a um: um único
    /// git worktree prune no final resolve todos. Nada usa --force.
    /// </summary>
    public async Task CleanupAsync(IReadOnlyList<WorktreeRow> rows)
    {
        if (rows.Count == 0) return;

        var repositoryPath = ExpandHome(RepositoryPath.Trim());
        var removed = new List<string>();
        var skipped = new List<string>();
        var orphans = 0;

        IsBusy = true;
        StatusMessage = "Removendo worktrees concluídos…";

        try
        {
            foreach (var row in rows)
            {
                if (row.Worktree.IsPrunable)
                {
                    orphans++;
                    continue;
                }

                try
                {
                    var result = await GitService
                        .RemoveWorktreeAsync(repositoryPath, row.FullPath)
                        .ConfigureAwait(true);

                    if (result.Success) removed.Add(row.Name);
                    else skipped.Add($"{row.Name}: {result.FirstErrorLine}");
                }
                catch (Exception exception)
                {
                    skipped.Add($"{row.Name}: {exception.Message}");
                }
            }

            if (orphans > 0)
            {
                try
                {
                    await GitService.PruneAsync(repositoryPath).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    skipped.Add($"prune: {exception.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        LastCleanupSkipped = skipped;

        await LoadAsync().ConfigureAwait(true);

        var parts = new List<string>();
        if (removed.Count > 0) parts.Add($"{removed.Count} removido(s)");
        if (orphans > 0) parts.Add($"{orphans} órfão(s) podado(s)");
        if (skipped.Count > 0) parts.Add($"{skipped.Count} mantido(s) — ver detalhes");
        if (parts.Count == 0) parts.Add("nada a fazer");

        StatusMessage = "Limpeza: " + string.Join(" · ", parts);
    }

    /// <summary>Worktrees que o git recusou remover (normalmente por alteração não commitada).</summary>
    public IReadOnlyList<string> LastCleanupSkipped { get; private set; } = Array.Empty<string>();

    /// <summary>Carrega os worktrees e, em seguida, enriquece a lista com os PRs.</summary>
    public async Task LoadAsync(string? selectPath = null)
    {
        var path = RepositoryPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "Informe o caminho de um repositório git.";
            return;
        }

        path = ExpandHome(path);

        if (!Directory.Exists(path))
        {
            StatusMessage = $"A pasta '{path}' não existe.";
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        IsBusy = true;
        HasStartedOnce = true;
        StatusMessage = "Lendo worktrees…";

        try
        {
            var worktrees = await GitService.ListWorktreesAsync(path, cancellationToken).ConfigureAwait(true);

            Worktrees.Clear();
            foreach (var worktree in worktrees)
                Worktrees.Add(new WorktreeRow(worktree));

            ApplyView();
            RaisePropertyChanged(nameof(MainWorktreePath));
            RaisePropertyChanged(nameof(CanCreateWorktree));
            SelectedWorktree = VisibleWorktrees.FirstOrDefault(row => PathsEqual(row.FullPath, selectPath))
                ?? VisibleWorktrees.FirstOrDefault();
            RaiseCleanupState();

            var root = await GitService.TryResolveRepositoryRootAsync(path, cancellationToken).ConfigureAwait(true);
            _settings.RepositoryPath = root ?? path;
            RepositoryPath = root ?? path;
            PersistSettings();

            StatusMessage = worktrees.Count switch
            {
                0 => "Nenhum worktree encontrado.",
                1 => "1 worktree. Buscando PRs…",
                _ => $"{worktrees.Count} worktrees. Buscando PRs…",
            };

            // O estado local de cada worktree é lido em paralelo com a consulta ao GitHub.
            var statusesTask = LoadStatusesAsync(Worktrees.ToList(), cancellationToken);

            var lookup = await GitHubService.LoadPullRequestsAsync(path, cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return;

            var matched = 0;
            foreach (var row in Worktrees)
            {
                if (row.Worktree.Branch is { } branch && lookup.ByBranch.TryGetValue(branch, out var pullRequest))
                {
                    row.PullRequest = pullRequest;
                    matched++;
                }

                row.RefreshTags();
            }

            RaiseCleanupState();

            // O PR chegou agora: um filtro por número/título ou a ordenação por PR mudam.
            ApplyView();

            await statusesTask.ConfigureAwait(true);

            var summary = Worktrees.Count == 1 ? "1 worktree" : $"{Worktrees.Count} worktrees";
            StatusMessage = lookup.Warning is { } warning
                ? $"{summary} · {warning}"
                : $"{summary} · {matched} com PR";
        }
        catch (OperationCanceledException)
        {
            // Outro carregamento tomou o lugar deste.
        }
        catch (Exception exception)
        {
            Worktrees.Clear();
            ApplyView();
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Caminho do worktree principal — é a partir dele que se cria um novo.</summary>
    public string? MainWorktreePath =>
        Worktrees.FirstOrDefault(row => row.Worktree.IsMain && !row.Worktree.IsBare)?.FullPath;

    public bool CanCreateWorktree => MainWorktreePath is not null;

    public string EffectiveCommand => string.IsNullOrWhiteSpace(Command) ? "claude" : Command.Trim();

    /// <summary>Depois de criado: recarrega, seleciona o novo e, se pedido, abre o terminal nele.</summary>
    public async Task CompleteCreationAsync(WorktreeCreationResult result, bool openTerminal)
    {
        // Se o filtro esconderia o worktree novo, é ele que sai do caminho.
        FilterText = string.Empty;

        await LoadAsync(selectPath: result.Path).ConfigureAwait(true);

        var created = VisibleWorktrees.FirstOrDefault(row => PathsEqual(row.FullPath, result.Path));
        if (created is not null) SelectedWorktree = created;

        if (openTerminal && created is not null) await LaunchAsync(created).ConfigureAwait(true);

        var parts = new List<string> { $"Worktree {Path.GetFileName(result.Path)} criado na branch {result.Branch}" };
        if (result.CopiedFiles > 0) parts.Add($"{result.CopiedFiles} arquivo(s) copiado(s) do {WorktreeCreator.IncludeFileName}");
        parts.AddRange(result.Warnings);

        StatusMessage = string.Join(" · ", parts);
    }

    public async Task LaunchAsync(WorktreeRow? row)
    {
        if (row is null) return;

        var command = EffectiveCommand;

        try
        {
            await TerminalLauncher.LaunchAsync(row.FullPath, command).ConfigureAwait(true);
            StatusMessage = $"{TerminalLauncher.TerminalName} aberto em {row.Name} · {command}";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task RevealAsync(WorktreeRow? row)
    {
        if (row is null) return;

        try
        {
            await TerminalLauncher.RevealInFinderAsync(row.FullPath).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task OpenPullRequestAsync(WorktreeRow? row)
    {
        if (row?.PullRequest is not { } pullRequest || string.IsNullOrEmpty(pullRequest.Url)) return;

        try
        {
            await TerminalLauncher.OpenUrlAsync(pullRequest.Url).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public void SetRepositoryPath(string path)
    {
        RepositoryPath = path;
        _settings.RepositoryPath = path;
        PersistSettings();
    }

    /// <summary>
    /// Lê `git status` de cada worktree. Limitado a 8 de cada vez para não disparar
    /// dezenas de processos git de uma vez em repositórios com muitos worktrees.
    /// </summary>
    private static async Task LoadStatusesAsync(
        IReadOnlyList<WorktreeRow> rows,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(8);

        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                row.Status = await WorktreeStatusReader
                    .ReadAsync(row.FullPath, cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    private void PersistSettings()
    {
        _settings.Command = _command;
        SettingsStore.Save(_settings);
    }

    private static bool PathsEqual(string left, string? right)
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

public enum SortColumn { Name, Branch, PullRequest, Path }

internal static class OrderingExtensions
{
    public static IOrderedEnumerable<T> Then<T, TKey>(
        this IOrderedEnumerable<T> source,
        Func<T, TKey> key,
        bool descending)
        => descending ? source.ThenByDescending(key) : source.ThenBy(key);
}
