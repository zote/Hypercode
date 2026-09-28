using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Hypercode.Services;

namespace Hypercode.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings;
    private readonly PullRequestMemory _pullRequestMemory = PullRequestMemory.Load();
    private CancellationTokenSource? _loadCancellation;
    private RepositoryWatcher? _watcher;
    private RepositoryChange _pendingChange;
    private bool _isRefreshing;

    /// <summary>Repositório do último carregamento bem-sucedido — é o que o watcher observa.</summary>
    private string? _loadedRepositoryPath;

    private string _repositoryPath = string.Empty;
    private string _command = "claude";
    private string _statusMessage = "Escolha um repositório git para listar os worktrees.";
    private bool _isBusy;
    private string _filterText = string.Empty;
    private SortColumn _sortColumn;
    private bool _sortDescending;

    /// <summary>
    /// De quanto em quanto tempo o laço do monitoramento pergunta ao agendador se algo venceu.
    /// A pergunta é só conta em memória; o custo está no que vence, não no tique.
    /// </summary>
    private static readonly TimeSpan MonitorTick = TimeSpan.FromSeconds(10);

    private readonly MonitorScheduler _scheduler = new();
    private CancellationTokenSource? _monitorCancellation;
    private bool _isCheckingRemote;
    private string? _monitorProblem;
    private string? _monitorNotice;

    /// <summary>
    /// O fetch do monitoramento em andamento. Pull e atualização a partir da base esperam por
    /// ele: dois fetches no mesmo repositório disputam o lock das refs remotas.
    /// </summary>
    private Task _backgroundFetch = Task.CompletedTask;

    public MainViewModel()
    {
        _settings = SettingsStore.Load();
        _repositoryPath = _settings.RepositoryPath ?? string.Empty;
        _command = string.IsNullOrWhiteSpace(_settings.Command) ? "claude" : _settings.Command;
        _sortColumn = ParseSortColumn(_settings.SortColumn);
        _sortDescending = _settings.SortDescending;
        _scheduler.Profile = ParseMonitorProfile(_settings.MonitorProfile);

        SelectedWorktrees.CollectionChanged += OnSelectionChanged;
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
            // Pelo caminho, não pela instância: um recarregamento troca todas as linhas.
            var selectedPaths = SelectedWorktrees.Select(row => row.FullPath).ToList();

            VisibleWorktrees.Clear();
            foreach (var row in result) VisibleWorktrees.Add(row);

            SelectPaths(selectedPaths);
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

    private static MonitorProfile ParseMonitorProfile(string? value)
        => Enum.TryParse<MonitorProfile>(value, ignoreCase: true, out var profile) ? profile : MonitorProfile.Balanced;

    /// <summary>
    /// Perfil do monitoramento, como índice do seletor do rodapé: desligado, econômico,
    /// equilibrado, agressivo — a ordem de <see cref="Services.MonitorProfile"/>.
    /// </summary>
    public int MonitorProfileIndex
    {
        get => (int)_scheduler.Profile;
        set
        {
            if (!Enum.IsDefined(typeof(MonitorProfile), value) || (int)_scheduler.Profile == value) return;

            _scheduler.Profile = (MonitorProfile)value;
            _settings.MonitorProfile = _scheduler.Profile.ToString().ToLowerInvariant();
            PersistSettings();
            RaisePropertyChanged();

            // Ficou mais atento: o que passou a estar vencido é conferido já.
            _ = CheckRemoteAsync();
        }
    }

    /// <summary>Aviso fixo do rodapé enquanto o monitoramento recua pela cota do GitHub.</summary>
    public string? MonitorNotice
    {
        get => _monitorNotice;
        private set
        {
            if (SetProperty(ref _monitorNotice, value)) RaisePropertyChanged(nameof(HasMonitorNotice));
        }
    }

    public bool HasMonitorNotice => _monitorNotice is not null;

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

    /// <summary>
    /// Linhas selecionadas na lista (⌘-clique e ⇧-clique), na ordem em que foram escolhidas.
    /// É o alvo do menu de contexto; ligada ao SelectedItems do ListBox.
    /// </summary>
    public ObservableCollection<WorktreeRow> SelectedWorktrees { get; } = new();

    /// <summary>
    /// A primeira linha selecionada — o alvo das ações de uma linha só (Enter, duplo-clique,
    /// botão do rodapé). Atribuir troca a seleção inteira por ela.
    /// </summary>
    public WorktreeRow? SelectedWorktree
    {
        get => SelectedWorktrees.FirstOrDefault();
        set => ReplaceSelection(value is null ? Array.Empty<WorktreeRow>() : new[] { value });
    }

    /// <summary>A seleção na ordem da lista, que é a ordem dos diálogos e relatórios.</summary>
    public IReadOnlyList<WorktreeRow> SelectedRowsInViewOrder()
        => VisibleWorktrees.Where(SelectedWorktrees.Contains).ToList();

    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RaisePropertyChanged(nameof(SelectedWorktree));

    /// <summary>Seleciona as linhas visíveis desses caminhos; se nenhuma sobrou, a primeira.</summary>
    private void SelectPaths(IReadOnlyCollection<string> paths)
    {
        var rows = VisibleWorktrees.Where(row => paths.Any(path => PathsEqual(row.FullPath, path))).ToList();
        if (rows.Count == 0 && VisibleWorktrees.FirstOrDefault() is { } first) rows.Add(first);

        ReplaceSelection(rows);
    }

    private void ReplaceSelection(IReadOnlyList<WorktreeRow> rows)
    {
        if (rows.SequenceEqual(SelectedWorktrees)) return;

        SelectedWorktrees.Clear();
        foreach (var row in rows) SelectedWorktrees.Add(row);
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

    /// <summary>Texto do diálogo de confirmação de "Apagar o worktree".</summary>
    public static string BuildRemovalSummary(WorktreeRow row)
    {
        var lines = new List<string> { $"{row.Name}  —  {row.Branch}", $"    {row.FullPath}" };

        if (row.Worktree.IsPrunable)
            lines.Add("\nA pasta já não existe: só os metadados do git são limpos, com git worktree prune — que poda todos os órfãos de uma vez.");
        else if (row.Worktree.IsLocked)
            lines.Add("\nEstá travado (git worktree lock): o git vai recusar. Destrave antes, pelo menu Destravar o worktree….");
        else if (row.Status.HasUncommittedChanges)
            lines.Add("\nHá alterações não commitadas: o git vai recusar, e aí o app pergunta se é para forçar.");

        if (row.PullRequest is { IsOpen: true } pullRequest)
            lines.Add($"\nO PR #{pullRequest.Number} ainda está aberto.");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Texto do diálogo de "Apagar os worktrees" em lote, no formato do da limpeza. Em lote
    /// não há segunda confirmação para forçar: o worktree sujo ou travado fica e vai para o relatório.
    /// </summary>
    public static string BuildBatchRemovalSummary(IReadOnlyList<WorktreeRow> rows)
        => string.Join(
            "\n",
            rows.Select(row =>
            {
                var notes = new List<string>();
                if (row.Worktree.IsPrunable) notes.Add("órfão: só o prune");
                else if (row.Worktree.IsLocked) notes.Add("travado: o git vai recusar");
                else if (row.Status.HasUncommittedChanges) notes.Add("alterações não commitadas: o git vai recusar");
                if (row.PullRequest is { IsOpen: true } pullRequest) notes.Add($"PR #{pullRequest.Number} ainda aberto");

                return $"{row.Name}  —  {row.Branch}\n"
                       + (notes.Count > 0 ? $"    {string.Join(" · ", notes)}\n" : string.Empty)
                       + $"    {row.FullPath}\n";
            }));

    /// <summary>
    /// Remove os worktrees concluídos. Os órfãos não são removidos um a um: um único
    /// git worktree prune no final resolve todos. Nada usa --force.
    /// </summary>
    public async Task CleanupAsync(IReadOnlyList<WorktreeRow> rows)
    {
        if (rows.Count == 0) return;

        // O git recusa remover worktree travado. Como só chegam aqui os de lock de
        // ferramenta (o manual fica de fora), destravamos antes.
        var outcome = await RemoveManyAsync(rows, "Removendo worktrees concluídos…", unlockFirst: true).ConfigureAwait(true);

        LastCleanupSkipped = outcome.Failed;

        var parts = new List<string>();
        if (outcome.Removed > 0) parts.Add($"{outcome.Removed} removido(s)");
        if (outcome.Orphans > 0) parts.Add($"{outcome.Orphans} órfão(s) podado(s)");
        if (outcome.Failed.Count > 0) parts.Add($"{outcome.Failed.Count} mantido(s) — ver detalhes");
        if (parts.Count == 0) parts.Add("nada a fazer");

        StatusMessage = "Limpeza: " + string.Join(" · ", parts);
    }

    /// <summary>
    /// Núcleo da limpeza e do "Apagar os worktrees" em lote. Um de cada vez — todos mexem nos
    /// metadados do mesmo repositório —, órfãos com um prune só no fim, sem --force, e a lista
    /// relida ao terminar.
    /// </summary>
    private async Task<RemovalOutcome> RemoveManyAsync(IReadOnlyList<WorktreeRow> rows, string activity, bool unlockFirst)
    {
        var repositoryPath = ExpandHome(RepositoryPath.Trim());
        var removed = new List<string>();
        var failed = new List<string>();
        var orphans = new List<string>();

        IsBusy = true;
        StatusMessage = activity;

        try
        {
            foreach (var row in rows)
            {
                if (row.Worktree.IsPrunable)
                {
                    orphans.Add(row.Name);
                    continue;
                }

                try
                {
                    if (unlockFirst && row.Worktree.IsLocked)
                        await GitService
                            .UnlockWorktreeAsync(repositoryPath, row.FullPath)
                            .ConfigureAwait(true);

                    var result = await GitService
                        .RemoveWorktreeAsync(repositoryPath, row.FullPath)
                        .ConfigureAwait(true);

                    if (result.Success) removed.Add(row.Name);
                    else failed.Add($"{row.Name}: {result.FirstErrorLine}");
                }
                catch (Exception exception)
                {
                    failed.Add($"{row.Name}: {exception.Message}");
                }
            }

            if (orphans.Count > 0)
            {
                try
                {
                    var prune = await GitService.PruneAsync(repositoryPath).ConfigureAwait(true);
                    if (!prune.Success)
                    {
                        failed.Add($"prune: {prune.FirstErrorLine}");
                        orphans.Clear();
                    }
                }
                catch (Exception exception)
                {
                    failed.Add($"prune: {exception.Message}");
                    orphans.Clear();
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);

        return new RemovalOutcome(removed, orphans, failed);
    }

    private sealed record RemovalOutcome(IReadOnlyList<string> RemovedNames, IReadOnlyList<string> OrphanNames, IReadOnlyList<string> Failed)
    {
        public int Removed => RemovedNames.Count;
        public int Orphans => OrphanNames.Count;
    }

    /// <summary>Worktrees que o git recusou remover (normalmente por alteração não commitada).</summary>
    public IReadOnlyList<string> LastCleanupSkipped { get; private set; } = Array.Empty<string>();

    /// <summary>Carrega os worktrees e, em seguida, enriquece a lista com os PRs.</summary>
    public Task LoadAsync(string? selectPath = null) => LoadCoreAsync(selectPath, background: false);

    /// <summary>
    /// Em background (disparado pelo <see cref="RepositoryWatcher"/>) o carregamento é
    /// silencioso: não trava os botões, não mexe no rodapé, usa o repositório do último
    /// carregamento — não o que estiver meio digitado no campo — e, se o git devolver a
    /// mesma lista, para por aí. Devolve se a lista foi refeita.
    /// </summary>
    private async Task<bool> LoadCoreAsync(string? selectPath, bool background)
    {
        var path = background ? _loadedRepositoryPath : RepositoryPath.Trim();
        if (string.IsNullOrEmpty(path))
        {
            if (!background) StatusMessage = "Informe o caminho de um repositório git.";
            return false;
        }

        path = ExpandHome(path);

        if (!Directory.Exists(path))
        {
            if (!background) StatusMessage = $"A pasta '{path}' não existe.";
            return false;
        }

        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        if (!background)
        {
            IsBusy = true;
            HasStartedOnce = true;
            StatusMessage = "Lendo worktrees…";
        }

        try
        {
            var worktrees = await GitService.ListWorktreesAsync(path, cancellationToken).ConfigureAwait(true);

            if (background && worktrees.SequenceEqual(Worktrees.Select(row => row.Worktree))) return false;

            // Linha que não mudou herda PR e estado: a lista não pisca enquanto relê.
            var previous = Worktrees.ToDictionary(row => row.Worktree);

            Worktrees.Clear();
            foreach (var worktree in worktrees)
            {
                var row = new WorktreeRow(worktree);
                if (previous.TryGetValue(worktree, out var old))
                {
                    row.PullRequest = old.PullRequest;
                    row.PullRequestChanges = old.PullRequestChanges;
                    row.Status = old.Status;
                    row.BaseDistance = old.BaseDistance;
                }

                Worktrees.Add(row);
            }

            _scheduler.Retain(LocalBranches());

            ApplyView();
            RaisePropertyChanged(nameof(MainWorktreePath));
            RaisePropertyChanged(nameof(CanCreateWorktree));
            // O ApplyView já manteve a seleção de antes; um caminho pedido a substitui.
            if (selectPath is not null) SelectPaths(new[] { selectPath });
            RaiseCleanupState();

            if (!background)
            {
                var root = await GitService.TryResolveRepositoryRootAsync(path, cancellationToken).ConfigureAwait(true);
                _settings.RepositoryPath = root ?? path;
                RepositoryPath = root ?? path;
                PersistSettings();
                if (_loadedRepositoryPath != path) _scheduler.Reset();
                _loadedRepositoryPath = path;

                await WatchAsync(path, cancellationToken).ConfigureAwait(true);
                StartMonitoring();

                StatusMessage = worktrees.Count switch
                {
                    0 => "Nenhum worktree encontrado.",
                    1 => "1 worktree. Buscando PRs…",
                    _ => $"{worktrees.Count} worktrees. Buscando PRs…",
                };
            }

            // O estado local de cada worktree é lido em paralelo com a consulta ao GitHub.
            var statusesTask = LoadStatusesAsync(Worktrees.ToList(), cancellationToken);

            var lookup = await GitHubService.LoadPullRequestsAsync(path, WorktreeBranches(), cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return true;

            // Carregamento completo conta como conferência de todo mundo: o agendador parte daqui.
            if (!lookup.Failed)
            {
                _scheduler.MarkChecked(LocalBranches(), DateTimeOffset.UtcNow);
                _scheduler.RecordBudget(lookup.Budget);
                UpdateMonitorNotice();
            }

            // Em background, uma consulta que falhou não apaga os PRs herdados das linhas.
            var (matched, changes) = background && lookup.Failed
                ? (Worktrees.Count(row => row.HasPullRequest), null)
                : ApplyPullRequests(lookup);

            if (background && changes is not null) StatusMessage = changes;

            // Só agora se sabe a base de cada PR, e com ela a distância até ela.
            await Task.WhenAll(statusesTask, LoadBaseDistancesAsync(Worktrees.ToList(), cancellationToken))
                .ConfigureAwait(true);

            if (!background)
            {
                var summary = Worktrees.Count == 1 ? "1 worktree" : $"{Worktrees.Count} worktrees";
                StatusMessage = (lookup.Warning is { } warning
                    ? $"{summary} · {warning}"
                    : $"{summary} · {matched} com PR")
                    + (changes is null ? string.Empty : $" · {changes}");
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // Outro carregamento tomou o lugar deste.
            return false;
        }
        catch (Exception exception)
        {
            // Em background, uma falha passageira não apaga a lista que já está na tela.
            if (background) return false;

            Worktrees.Clear();
            ApplyView();
            StopWatching();
            StopMonitoring();
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            if (!background) IsBusy = false;
        }
    }

    /// <summary>Nomes a consultar no GitHub: o da branch no remoto e o local, para quem não tem upstream.</summary>
    private IReadOnlyCollection<string> WorktreeBranches() => QueryNames(Worktrees);

    private static IReadOnlyCollection<string> QueryNames(IEnumerable<WorktreeRow> rows)
        => rows
            .SelectMany(row => new[] { row.Worktree.UpstreamBranch, row.Worktree.Branch })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Branches locais dos worktrees — a chave do agendador.</summary>
    private IReadOnlyCollection<string> LocalBranches()
        => Worktrees.Select(row => row.Worktree.Branch).OfType<string>().ToList();

    /// <summary>
    /// PR da linha: primeiro pelo nome da branch no remoto, depois pelo local. O upstream é
    /// ignorado quando é a branch local de outro worktree — branch criada de origin/main sem
    /// --no-track rastreia main, e o PR de main é da linha do main.
    /// </summary>
    private static PullRequestInfo? FindPullRequest(WorktreeInfo worktree, PullRequestLookup lookup, ISet<string> localBranches)
    {
        if (worktree.UpstreamBranch is { } upstream
            && upstream != worktree.Branch
            && !localBranches.Contains(upstream)
            && lookup.ByBranch.TryGetValue(upstream, out var byUpstream))
            return byUpstream;

        return worktree.Branch is { } branch && lookup.ByBranch.TryGetValue(branch, out var byBranch)
            ? byBranch
            : null;
    }

    /// <summary>
    /// Casa os PRs com as linhas pela branch, compara cada um com o último estado visto e
    /// avisa das transições. É o caminho comum do carregamento e do monitoramento — por isso
    /// uma mudança que aconteceu com o app fechado também é avisada, uma vez, ao abrir.
    /// Com <paramref name="scope"/>, só as linhas dessas branches são tocadas — o monitoramento
    /// consulta um lote, não a lista inteira. Devolve quantas linhas (do escopo) têm PR e, se
    /// algum mudou, a frase para o rodapé.
    /// </summary>
    private (int Matched, string? Changes) ApplyPullRequests(PullRequestLookup lookup, IReadOnlySet<string>? scope = null)
    {
        var matched = 0;
        var changed = new List<(WorktreeRow Row, PullRequestInfo PullRequest, IReadOnlyList<string> Transitions)>();

        var localBranches = Worktrees.Select(row => row.Worktree.Branch).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Worktrees)
        {
            if (scope is not null && (row.Worktree.Branch is not { } scoped || !scope.Contains(scoped))) continue;

            row.PullRequest = FindPullRequest(row.Worktree, lookup, localBranches);

            if (row.PullRequest is { } pullRequest)
            {
                matched++;
                var transitions = _pullRequestMemory.Observe(pullRequest);
                if (transitions.Count > 0) changed.Add((row, pullRequest, transitions));
                row.PullRequestChanges = _pullRequestMemory.Unseen(pullRequest);
            }
            else
            {
                row.PullRequestChanges = Array.Empty<string>();
            }

            row.RefreshTags();
        }

        _pullRequestMemory.Save();
        RaiseCleanupState();

        // O PR chegou agora: um filtro por número/título ou a ordenação por PR mudam.
        ApplyView();

        return (matched, changed.Count > 0 ? AnnounceChanges(changed) : null);
    }

    /// <summary>Notificação do macOS — acima de três PRs, uma só — e a frase para o rodapé.</summary>
    private string AnnounceChanges(IReadOnlyList<(WorktreeRow Row, PullRequestInfo PullRequest, IReadOnlyList<string> Transitions)> changed)
    {
        static string Line((WorktreeRow Row, PullRequestInfo PullRequest, IReadOnlyList<string> Transitions) change)
            => $"#{change.PullRequest.Number} {string.Join(" · ", change.Transitions)}";

        var summary = changed.Count == 1
            ? $"PR {Line(changed[0])}"
            : $"{changed.Count} PRs mudaram: " + string.Join("; ", changed.Select(Line));

        if (!_settings.NotifyPullRequestChanges) return summary;

        if (changed.Count <= 3)
        {
            foreach (var change in changed)
                _ = Notifier.NotifyAsync(
                    $"#{change.PullRequest.Number} · {change.Row.Branch}",
                    string.Join(" · ", change.Transitions));
        }
        else
        {
            _ = Notifier.NotifyAsync($"{changed.Count} PRs mudaram", string.Join("\n", changed.Select(Line)));
        }

        return summary;
    }

    /// <summary>
    /// Tira o destaque de PR alterado das linhas. Abrir o PR no navegador também conta como
    /// ter visto.
    /// </summary>
    public void MarkPullRequestChangesSeen(IReadOnlyList<WorktreeRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.PullRequest is not { } pullRequest || !row.HasPullRequestChanges) continue;
            _pullRequestMemory.MarkSeen(pullRequest);
            row.PullRequestChanges = Array.Empty<string>();
        }

        _pullRequestMemory.Save();
    }

    /// <summary>
    /// A janela avisa quando ganha ou perde o foco e quando é minimizada ou restaurada. Em
    /// segundo plano as cadências triplicam; minimizada, nada roda.
    /// </summary>
    public void SetWindowActivity(bool isActive, bool isMinimized)
    {
        _scheduler.IsWindowActive = isActive;
        _scheduler.IsWindowMinimized = isMinimized;

        // Voltou para a frente: o que venceu enquanto estava fora é conferido já, sem esperar o tique.
        if (isActive && !isMinimized) _ = CheckRemoteAsync();
    }

    private void StartMonitoring()
    {
        if (_monitorCancellation is not null) return;

        _monitorCancellation = new CancellationTokenSource();
        _ = MonitorLoopAsync(_monitorCancellation.Token);
    }

    private void StopMonitoring()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation = null;
    }

    /// <summary>
    /// Roda na UI enquanto houver repositório carregado. Com o perfil desligado ou a janela
    /// minimizada o laço segue, mas o agendador não deixa nada vencer.
    /// </summary>
    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(MonitorTick);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
                await CheckRemoteAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Monitoramento parado.
        }
    }

    /// <summary>As branches dos worktrees com a faixa de cadência de cada uma, pelo estado de agora.</summary>
    private IEnumerable<ScheduledBranch> ScheduledBranches()
        => Worktrees
            .Where(row => row.Worktree.Branch is not null && !row.Worktree.IsBare)
            .Select(row => new ScheduledBranch(
                row.Worktree.Branch!,
                MonitorScheduler.Classify(row.PullRequest, row.Status, row.Worktree.IsMain),
                row.PullRequest is not null));

    /// <summary>
    /// Um passo do monitoramento: o `git fetch --all --prune`, se venceu o ciclo dele, e uma
    /// consulta só dos PRs das branches que venceram — em lote, nunca uma por worktree. O fetch
    /// escreve em refs/remotes, e o <see cref="RepositoryWatcher"/> relê o status das linhas
    /// sozinho. Não roda com uma operação do app em andamento; sem gh, fica só a parte git.
    /// </summary>
    private async Task CheckRemoteAsync()
    {
        UpdateMonitorNotice();

        if (_isCheckingRemote || IsBusy || _loadedRepositoryPath is not { } path) return;

        var now = DateTimeOffset.UtcNow;
        var fetchDue = _scheduler.IsFetchDue(now);
        var due = _scheduler.DueBranches(ScheduledBranches(), now);
        if (!fetchDue && due.Count == 0) return;

        _isCheckingRemote = true;
        var cancellationToken = _monitorCancellation?.Token ?? default;
        var problems = new List<string>();

        try
        {
            if (fetchDue)
            {
                _scheduler.MarkFetched(now);
                var fetch = GitService.FetchAllInBackgroundAsync(path, cancellationToken);
                _backgroundFetch = fetch;

                try
                {
                    var result = await fetch.ConfigureAwait(true);
                    if (!result.Success) problems.Add($"git fetch falhou: {result.FirstErrorLine}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    problems.Add($"git fetch falhou: {exception.Message}");
                }

                // O usuário começou algo durante o fetch: os PRs ficam para o próximo tique.
                if (IsBusy) return;
            }

            if (due.Count == 0) return;

            // Marca antes de consultar: uma consulta que falha não é repetida a cada tique.
            _scheduler.MarkChecked(due, now);

            // O agendador fala em branch local; a consulta leva também o nome no remoto.
            var scope = due.ToHashSet(StringComparer.Ordinal);
            var rows = Worktrees.Where(row => row.Worktree.Branch is { } branch && scope.Contains(branch)).ToList();

            var lookup = await GitHubService.LoadPullRequestsAsync(path, QueryNames(rows), cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || _loadedRepositoryPath != path) return;

            if (lookup.Failed)
            {
                problems.Add(lookup.Warning ?? "não consegui ler os PRs");
                return;
            }

            _scheduler.RecordBudget(lookup.Budget);

            if (ApplyPullRequests(lookup, scope).Changes is { } changes) StatusMessage = changes;

            await LoadBaseDistancesAsync(rows, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Repositório trocado ou monitoramento desligado.
        }
        catch (Exception exception)
        {
            problems.Add(exception.Message);
        }
        finally
        {
            _isCheckingRemote = false;
            ReportMonitorProblem(problems.Count == 0 ? null : string.Join(" · ", problems));
            UpdateMonitorNotice();
        }
    }

    /// <summary>
    /// Enquanto o agendador recua pela cota, o rodapé diz — sem isso a coluna PR parada
    /// pareceria travamento. Some sozinho no reset da cota ou quando o consumo baixa.
    /// </summary>
    private void UpdateMonitorNotice()
    {
        var now = DateTimeOffset.UtcNow;
        MonitorNotice = _scheduler.Budget is { } budget && _scheduler.IsBackingOff(now) && !_scheduler.IsPaused
            ? $"Monitor em recuo · {budget.UsedFraction:P0} da cota do GitHub usada · normaliza às {budget.ResetAt.ToLocalTime():HH:mm}"
            : null;
    }

    /// <summary>
    /// Promove a branch no agendador quando o status relido mostra um push: ganhou upstream
    /// agora, ou tinha commits à frente e deixou de ter. É o momento em que um PR está para
    /// nascer — e o sinal já chega pelo watcher, sem custar chamada nenhuma.
    /// </summary>
    private void NotePush(WorktreeRow row, WorktreeStatus previous, WorktreeStatus current)
    {
        if (!previous.IsKnown || !current.IsKnown || row.PullRequest is not null || row.Worktree.Branch is not { } branch) return;

        var now = DateTimeOffset.UtcNow;
        if (!previous.HasUpstream && current.HasUpstream)
            _scheduler.Promote(branch, MonitorScheduler.UpstreamPromotion, now);
        else if (previous.HasUpstream && current.HasUpstream && previous.Ahead > 0 && current.Ahead == 0)
            _scheduler.Promote(branch, MonitorScheduler.PushPromotion, now);
    }

    /// <summary>Problema do monitoramento vai para o rodapé uma vez — não a cada ciclo que falhar igual.</summary>
    private void ReportMonitorProblem(string? problem)
    {
        if (problem == _monitorProblem) return;
        _monitorProblem = problem;
        if (problem is not null) StatusMessage = $"Monitoramento: {problem}";
    }

    /// <summary>Espera o fetch do monitoramento, se houver um rodando; o resultado dele não importa aqui.</summary>
    private async Task WaitForBackgroundFetchAsync()
    {
        try
        {
            await _backgroundFetch.ConfigureAwait(true);
        }
        catch
        {
            // Falha do fetch em background já foi relatada por ele.
        }
    }

    /// <summary>Passa a observar o git dir do repositório carregado, se ainda não observa.</summary>
    private async Task WatchAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gitDir = await GitService.TryResolveCommonGitDirAsync(repositoryPath, cancellationToken).ConfigureAwait(true);

        if (gitDir is not null && _watcher is not null && PathsEqual(_watcher.GitCommonDir, gitDir)) return;

        StopWatching();
        if (gitDir is null) return;

        try
        {
            _watcher = new RepositoryWatcher(gitDir, OnRepositoryChanged);
        }
        catch (Exception)
        {
            // Sem watcher o app segue funcionando: resta o botão Atualizar.
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Mudança vinda do watcher. Roda na UI; eventos que chegam durante uma releitura se
    /// acumulam e viram uma só releitura depois dela. Se o app está no meio de uma operação
    /// sua (carregar, limpar, apagar), espera: ela mesma relê a lista no fim.
    /// </summary>
    private async void OnRepositoryChanged(RepositoryChange change)
    {
        _pendingChange |= change;
        if (_isRefreshing) return;

        _isRefreshing = true;
        try
        {
            while (_pendingChange != RepositoryChange.None)
            {
                if (IsBusy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
                    continue;
                }

                var pending = _pendingChange;
                _pendingChange = RepositoryChange.None;

                // Se a lista foi refeita, o estado de todas as linhas já foi relido junto.
                if (pending.HasFlag(RepositoryChange.Worktrees)
                    && await LoadCoreAsync(selectPath: null, background: true).ConfigureAwait(true))
                    continue;

                if (pending.HasFlag(RepositoryChange.Status)) await RefreshStatusesAsync().ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Atualização automática é conveniência: se falhar, o botão Atualizar resolve.
            _pendingChange = RepositoryChange.None;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Relê só o `git status` das linhas — para commit, fetch, push, rebase…</summary>
    private async Task RefreshStatusesAsync()
    {
        try
        {
            // Um fetch pode ter trazido commits novos da base: a distância muda junto.
            var rows = Worktrees.ToList();
            var cancellationToken = _loadCancellation?.Token ?? default;
            await Task.WhenAll(
                    LoadStatusesAsync(rows, cancellationToken),
                    LoadBaseDistancesAsync(rows, cancellationToken))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Um carregamento completo começou e vai reler tudo.
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

    public Task LaunchAsync(WorktreeRow? row) => LaunchAsync(row, EffectiveCommand);

    /// <summary>Terminal na pasta do worktree, sem rodar comando nenhum.</summary>
    public Task OpenShellAsync(WorktreeRow? row) => LaunchAsync(row, command: null);

    /// <summary>Terminal retomando a última conversa do Claude Code naquele worktree.</summary>
    public Task ResumeClaudeAsync(WorktreeRow? row) => LaunchAsync(row, ClaudeSessions.ContinueCommand);

    private async Task LaunchAsync(WorktreeRow? row, string? command)
    {
        if (row is null) return;

        try
        {
            await TerminalLauncher.LaunchAsync(row.FullPath, command, TerminalTitle(row)).ConfigureAwait(true);
            StatusMessage = command is null
                ? $"{TerminalLauncher.TerminalName} aberto em {row.Name}"
                : $"{TerminalLauncher.TerminalName} aberto em {row.Name} · {command}";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    /// <summary>Título da janela do terminal: nome do worktree e, se diferente, a branch.</summary>
    internal static string TerminalTitle(WorktreeRow row)
        => row.Branch == row.Name ? row.Name : $"{row.Name} · {row.Branch}";

    /// <summary>
    /// Remove um worktree. O órfão sai com prune (a pasta já não existe); os demais com
    /// git worktree remove, com --force só se <paramref name="force"/> — que vem de uma
    /// segunda confirmação, depois de o git recusar. Devolve o erro do git, ou null se removeu.
    /// </summary>
    public async Task<string?> RemoveWorktreeAsync(WorktreeRow row, bool force = false)
    {
        if (!row.CanRemove) return null;

        var repositoryPath = ExpandHome(RepositoryPath.Trim());

        IsBusy = true;
        StatusMessage = $"Removendo {row.Name}…";

        string? error;
        try
        {
            var result = row.Worktree.IsPrunable
                ? await GitService.PruneAsync(repositoryPath).ConfigureAwait(true)
                : await GitService.RemoveWorktreeAsync(repositoryPath, row.FullPath, force).ConfigureAwait(true);

            error = result.Success ? null : result.FirstErrorLine;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            StatusMessage = $"{row.Name} não foi removido: {error}";
            return error;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Worktree {row.Name} removido · a branch {row.Branch} continua existindo";
        return null;
    }

    /// <summary>Texto do diálogo de confirmação de "Destravar o worktree": o worktree e a trava.</summary>
    public static string BuildUnlockSummary(WorktreeRow row)
    {
        var worktree = row.Worktree;
        var lines = new List<string>
        {
            "Worktree",
            $"    nome:    {row.Name}",
            $"    branch:  {row.Branch}",
            $"    caminho: {row.FullPath}",
            "",
            "Trava",
        };

        if (worktree.IsToolLock)
        {
            lines.Add("    tipo:    de ferramenta");
            lines.Add($"    dono:    {worktree.LockOwner}");
            lines.Add($"    registro: {worktree.LockReason}");
            lines.Add("");
            lines.Add($"É bookkeeping do {worktree.LockOwner}, não um pedido seu: destravar pode fazer a ferramenta perder o controle deste worktree.");
        }
        else
        {
            lines.Add("    tipo:    manual (git worktree lock)");
            lines.Add(string.IsNullOrWhiteSpace(worktree.LockReason)
                ? "    motivo:  nenhum registrado"
                : $"    motivo:  {worktree.LockReason}");
            lines.Add("");
            lines.Add("Destravado, o worktree volta a poder ser removido e podado, inclusive pela limpeza de concluídos.");
        }

        return string.Join("\n", lines);
    }

    /// <summary>git worktree lock numa linha. Devolve o erro do git, ou null se travou.</summary>
    public Task<string?> LockWorktreeAsync(WorktreeRow row, string? reason)
        => ChangeLockAsync(
            row,
            $"Travando {row.Name}…",
            path => GitService.LockWorktreeAsync(path, row.FullPath, reason),
            $"Worktree {row.Name} travado",
            $"{row.Name} não foi travado");

    /// <summary>git worktree unlock numa linha. Devolve o erro do git, ou null se destravou.</summary>
    public Task<string?> UnlockWorktreeAsync(WorktreeRow row)
        => ChangeLockAsync(
            row,
            $"Destravando {row.Name}…",
            path => GitService.UnlockWorktreeAsync(path, row.FullPath),
            $"Worktree {row.Name} destravado",
            $"{row.Name} não foi destravado");

    private async Task<string?> ChangeLockAsync(
        WorktreeRow row,
        string activity,
        Func<string, Task<ProcessResult>> run,
        string success,
        string failure)
    {
        var repositoryPath = ExpandHome(RepositoryPath.Trim());

        IsBusy = true;
        StatusMessage = activity;

        string? error;
        try
        {
            var result = await run(repositoryPath).ConfigureAwait(true);
            error = result.Success ? null : result.FirstErrorLine;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            StatusMessage = $"{failure}: {error}";
            return error;
        }

        // Relê a lista: a tag "travado", o tooltip e a elegibilidade para a limpeza mudam.
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = success;
        return null;
    }

    /// <summary>"Travar os worktrees" em lote: o mesmo motivo para todos os que ainda não estão travados.</summary>
    public async Task<BatchOutcome> LockWorktreesAsync(IReadOnlyList<WorktreeRow> rows, string? reason)
    {
        var repositoryPath = ExpandHome(RepositoryPath.Trim());

        var outcome = await RunBatchAsync(
            "Travar os worktrees",
            "Travando",
            rows,
            row => row.CanLock,
            concurrency: 4,
            async row =>
            {
                var result = await GitService.LockWorktreeAsync(repositoryPath, row.FullPath, reason).ConfigureAwait(true);
                if (!result.Success) throw new InvalidOperationException(result.FirstErrorLine);
                return null;
            }).ConfigureAwait(true);

        if (outcome.Succeeded.Count > 0)
        {
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = outcome.Summary;
        }

        return outcome;
    }

    /// <summary>git pull --ff-only no worktree e relê o estado da linha.</summary>
    public async Task UpdateBranchAsync(WorktreeRow? row)
    {
        if (row is null || !row.CanUpdateBranch) return;

        StatusMessage = $"Atualizando {row.Branch}…";

        try
        {
            await WaitForBackgroundFetchAsync().ConfigureAwait(true);
            var result = await GitService.PullFastForwardAsync(row.FullPath).ConfigureAwait(true);

            StatusMessage = result.Success
                ? $"{row.Branch} atualizada" + (LastLine(result.StandardOutput) is { Length: > 0 } line ? $" · {line}" : "")
                : $"{row.Branch} não foi atualizada: {result.FirstErrorLine}";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }

        row.Status = await WorktreeStatusReader.ReadAsync(row.FullPath).ConfigureAwait(true);
    }

    /// <summary>
    /// Primeira etapa de "atualizar a partir da base": fetch da base, recusa com worktree
    /// sujo ou operação git parada no meio, e conta os commits que vão entrar. Não muda
    /// nada na branch — o que acontece depois depende da escolha entre merge e rebase.
    /// </summary>
    public async Task<BaseUpdatePlan> PrepareBaseUpdateAsync(WorktreeRow row)
    {
        if (!row.CanUpdateFromBase || row.Worktree.Branch is not { } branch || row.BaseBranch is not { } baseBranch)
            return BaseUpdatePlan.Refused("Esta linha não tem PR aberto com a base à frente.");

        IsBusy = true;
        StatusMessage = $"Buscando {baseBranch} para comparar com {branch}…";

        try
        {
            var status = await WorktreeStatusReader.ReadAsync(row.FullPath).ConfigureAwait(true);
            row.Status = status;

            if (status.PendingOperation is { } operation)
                return BaseUpdatePlan.Refused(
                    $"Há um {operation} parado no meio em {row.Name}. Conclua com git {operation} --continue ou desfaça com git {operation} --abort antes.");

            if (await GitService.HasTrackedChangesAsync(row.FullPath).ConfigureAwait(true))
                return BaseUpdatePlan.Refused(
                    $"{row.Name} tem alterações não commitadas em arquivos versionados — merge e rebase recusam assim. Faça commit ou stash antes.");

            var remote = await GitService.ResolveRemoteAsync(row.FullPath, branch).ConfigureAwait(true);

            await WaitForBackgroundFetchAsync().ConfigureAwait(true);
            var fetch = await GitService.FetchBranchAsync(row.FullPath, remote, baseBranch).ConfigureAwait(true);
            if (!fetch.Success)
                return BaseUpdatePlan.Refused($"git fetch {remote} {baseBranch} falhou: {fetch.FirstErrorLine}");

            var counted = await GitService.CountDistanceAsync(row.FullPath, $"{remote}/{baseBranch}").ConfigureAwait(true);
            if (counted is not { } count)
                return BaseUpdatePlan.Refused($"Não consegui comparar {branch} com {remote}/{baseBranch}.");

            var distance = new BaseDistance(remote, baseBranch, count.Behind, count.Ahead);
            row.BaseDistance = distance;
            return new BaseUpdatePlan(distance, null);
        }
        catch (Exception exception)
        {
            return BaseUpdatePlan.Refused(exception.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// Segunda etapa: git merge ou git rebase de &lt;remoto&gt;/&lt;base&gt;. Em conflito não tenta
    /// resolver nada — o worktree fica onde o git parou, e o ícone de operação pausada
    /// aparece na linha. Devolve o que dizer a quem pediu.
    /// </summary>
    public async Task<BaseUpdateOutcome> UpdateFromBaseAsync(WorktreeRow row, BaseDistance distance, BaseUpdateStrategy strategy)
    {
        var verb = strategy == BaseUpdateStrategy.Rebase ? "rebase" : "merge";

        IsBusy = true;
        StatusMessage = $"git {verb} {distance.Ref} em {row.Name}…";

        BaseUpdateOutcome outcome;
        try
        {
            var result = await GitService.IntegrateBaseAsync(row.FullPath, distance.Ref, strategy).ConfigureAwait(true);
            var status = await WorktreeStatusReader.ReadAsync(row.FullPath).ConfigureAwait(true);
            row.Status = status;

            if (result.Success)
            {
                outcome = strategy == BaseUpdateStrategy.Rebase
                    ? new BaseUpdateOutcome(true,
                        $"{row.Branch} rebaseada sobre {distance.Ref}",
                        "Os commits da branch foram reescritos. O próximo push precisa de git push --force-with-lease — um push comum será recusado.")
                    : new BaseUpdateOutcome(true, $"{distance.Ref} mergeada em {row.Branch} · falta o push", null);
            }
            else if (status.PendingOperation is { } operation)
            {
                outcome = new BaseUpdateOutcome(false,
                    $"{verb} parou em conflito em {row.Name}",
                    $"O git parou o {operation} com conflito e o worktree ficou nesse estado. Resolva os arquivos e rode git {operation} --continue, ou desfaça com git {operation} --abort.\n\n{Trim(result)}");
            }
            else
            {
                outcome = new BaseUpdateOutcome(false, $"{verb} recusado em {row.Name}: {result.FirstErrorLine}", Trim(result));
            }
        }
        catch (Exception exception)
        {
            outcome = new BaseUpdateOutcome(false, exception.Message, null);
        }
        finally
        {
            IsBusy = false;
        }

        await LoadBaseDistancesAsync(new[] { row }, default).ConfigureAwait(true);
        StatusMessage = outcome.Summary;
        return outcome;

        static string Trim(ProcessResult result)
            => (result.StandardOutput + "\n" + result.StandardError).Trim();
    }

    private static string LastLine(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .LastOrDefault() ?? string.Empty;

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
            MarkPullRequestChangesSeen(new[] { row });
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    /// <summary>
    /// Acima disto, abrir uma janela por worktree (terminal, navegador) pede confirmação:
    /// três terminais de uma vez é um caso real, sessenta por engano não é.
    /// </summary>
    public const int WindowConfirmationThreshold = 3;

    /// <summary>Mesmo limite de processos git simultâneos da leitura de estado.</summary>
    private const int BatchConcurrency = 8;

    public Task<BatchOutcome> LaunchManyAsync(IReadOnlyList<WorktreeRow> rows)
        => LaunchManyAsync("Abrir no iTerm2", rows, row => row.CanLaunch, EffectiveCommand);

    public Task<BatchOutcome> OpenShellManyAsync(IReadOnlyList<WorktreeRow> rows)
        => LaunchManyAsync("Abrir o terminal", rows, row => row.CanLaunch, command: null);

    public Task<BatchOutcome> ResumeClaudeManyAsync(IReadOnlyList<WorktreeRow> rows)
        => LaunchManyAsync("Retomar a sessão do claude", rows, row => row.HasClaudeSession, ClaudeSessions.ContinueCommand);

    // Uma janela de cada vez: o AppleScript do terminal não gosta de pedidos cruzados.
    private Task<BatchOutcome> LaunchManyAsync(string action, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports, string? command)
        => RunBatchAsync(action, "Abrindo", rows, supports, concurrency: 1, async row =>
        {
            await TerminalLauncher.LaunchAsync(row.FullPath, command, TerminalTitle(row)).ConfigureAwait(true);
            return null;
        });

    public Task<BatchOutcome> RevealManyAsync(IReadOnlyList<WorktreeRow> rows)
        => RunBatchAsync("Revelar no Finder", "Abrindo", rows, _ => true, concurrency: 1, async row =>
        {
            await TerminalLauncher.RevealInFinderAsync(row.FullPath).ConfigureAwait(true);
            return null;
        });

    public Task<BatchOutcome> OpenPullRequestsAsync(IReadOnlyList<WorktreeRow> rows)
        => RunBatchAsync("Abrir PR no navegador", "Abrindo", rows, row => row.HasPullRequest, concurrency: 1, async row =>
        {
            if (row.PullRequest is not { Url: { Length: > 0 } url }) throw new InvalidOperationException("o PR não tem URL");
            await TerminalLauncher.OpenUrlAsync(url).ConfigureAwait(true);
            MarkPullRequestChangesSeen(new[] { row });
            return null;
        });

    /// <summary>
    /// "Puxar do remoto" em lote. Equivale a um git pull --ff-only em cada worktree, mas com
    /// o fetch feito uma vez por remoto antes: pulls paralelos no mesmo repositório brigam
    /// pelo lock das refs remotas. Depois, o fast-forward de cada um roda em paralelo.
    /// </summary>
    public async Task<BatchOutcome> UpdateBranchesAsync(IReadOnlyList<WorktreeRow> rows)
    {
        var targets = rows.Where(row => row.CanUpdateBranch).ToList();
        var fetchErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var remoteOf = new Dictionary<WorktreeRow, string>();

        if (targets.Count > 0)
        {
            IsBusy = true;
            StatusMessage = "Buscando os remotos…";
            try
            {
                await WaitForBackgroundFetchAsync().ConfigureAwait(true);

                foreach (var row in targets)
                    remoteOf[row] = await GitService.ResolveRemoteAsync(row.FullPath, row.Worktree.Branch!).ConfigureAwait(true);

                foreach (var remote in remoteOf.Values.Distinct(StringComparer.Ordinal))
                {
                    StatusMessage = $"git fetch {remote}…";
                    try
                    {
                        var fetch = await GitService.FetchRemoteAsync(targets[0].FullPath, remote).ConfigureAwait(true);
                        if (!fetch.Success) fetchErrors[remote] = $"git fetch {remote} falhou: {fetch.FirstErrorLine}";
                    }
                    catch (Exception exception)
                    {
                        fetchErrors[remote] = $"git fetch {remote} falhou: {exception.Message}";
                    }
                }
            }
            catch (Exception exception)
            {
                IsBusy = false;
                StatusMessage = exception.Message;
                return new BatchOutcome("Puxar do remoto", Array.Empty<string>(), targets.Select(row => $"{row.Name}: {exception.Message}").ToList(),
                    rows.Where(row => !row.CanUpdateBranch).Select(row => row.Name).ToList());
            }
        }

        return await RunBatchAsync("Puxar do remoto", "Atualizando", rows, row => row.CanUpdateBranch, BatchConcurrency, async row =>
        {
            try
            {
                if (fetchErrors.TryGetValue(remoteOf[row], out var fetchError)) throw new InvalidOperationException(fetchError);

                var result = await GitService.MergeUpstreamFastForwardAsync(row.FullPath).ConfigureAwait(true);
                if (!result.Success) throw new InvalidOperationException(result.FirstErrorLine);

                return LastLine(result.StandardOutput);
            }
            finally
            {
                row.Status = await WorktreeStatusReader.ReadAsync(row.FullPath).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>"Apagar os worktrees" em lote: sem --force e sem destravar — o git recusa e o relatório diz.</summary>
    public async Task<BatchOutcome> RemoveWorktreesAsync(IReadOnlyList<WorktreeRow> rows)
    {
        var targets = rows.Where(row => row.CanRemove).ToList();
        var notApplicable = rows.Where(row => !row.CanRemove).Select(row => row.Name).ToList();

        var outcome = await RemoveManyAsync(targets, $"Removendo {targets.Count} worktree(s)…", unlockFirst: false).ConfigureAwait(true);

        var succeeded = outcome.RemovedNames
            .Concat(outcome.OrphanNames.Select(name => $"{name}: órfão podado"))
            .ToList();

        var batch = new BatchOutcome("Apagar os worktrees", succeeded, outcome.Failed, notApplicable);
        StatusMessage = batch.Summary + (succeeded.Count > 0 ? " · as branches continuam existindo" : string.Empty);
        return batch;
    }

    /// <summary>
    /// Roda <paramref name="action"/> nas linhas que a suportam, no máximo
    /// <paramref name="concurrency"/> de cada vez, com o progresso no rodapé. A ação devolve
    /// um detalhe para o relatório (ou null) e sinaliza falha lançando exceção.
    /// </summary>
    private async Task<BatchOutcome> RunBatchAsync(
        string action,
        string progressVerb,
        IReadOnlyList<WorktreeRow> rows,
        Func<WorktreeRow, bool> supports,
        int concurrency,
        Func<WorktreeRow, Task<string?>> run)
    {
        var targets = rows.Where(supports).ToList();
        var notApplicable = rows.Where(row => !supports(row)).Select(row => row.Name).ToList();
        var results = new (bool Success, string Line)[targets.Count];
        var done = 0;

        IsBusy = true;
        StatusMessage = $"{progressVerb} 0 de {targets.Count}…";

        try
        {
            using var gate = new SemaphoreSlim(concurrency);

            await Task.WhenAll(targets.Select(async (row, index) =>
            {
                await gate.WaitAsync().ConfigureAwait(true);
                try
                {
                    var detail = await run(row).ConfigureAwait(true);
                    results[index] = (true, string.IsNullOrEmpty(detail) ? row.Name : $"{row.Name}: {detail}");
                }
                catch (Exception exception)
                {
                    results[index] = (false, $"{row.Name}: {exception.Message}");
                }
                finally
                {
                    gate.Release();
                    StatusMessage = $"{progressVerb} {++done} de {targets.Count}…";
                }
            })).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        var outcome = new BatchOutcome(
            action,
            results.Where(result => result.Success).Select(result => result.Line).ToList(),
            results.Where(result => !result.Success).Select(result => result.Line).ToList(),
            notApplicable);

        StatusMessage = outcome.Summary;
        return outcome;
    }

    public void SetRepositoryPath(string path)
    {
        RepositoryPath = path;
        _settings.RepositoryPath = path;
        PersistSettings();
    }

    /// <summary>
    /// Lê `git status` de cada worktree. Limitado a 8 de cada vez para não disparar
    /// dezenas de processos git de uma vez em repositórios com muitos worktrees. O status
    /// também decide a limpeza (branch remota apagada já contida na base), daí recontar no fim.
    /// Um push visto na releitura promove a branch no agendador.
    /// </summary>
    private async Task LoadStatusesAsync(
        IReadOnlyList<WorktreeRow> rows,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(8);

        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                var previous = row.Status;
                row.Status = await WorktreeStatusReader
                    .ReadAsync(row.FullPath, cancellationToken)
                    .ConfigureAwait(true);
                NotePush(row, previous, row.Status);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(true);
        RaiseCleanupState();
    }

    /// <summary>
    /// Distância de cada linha com PR aberto até &lt;remoto&gt;/&lt;base&gt;, com as refs que já estão
    /// no repositório (sem fetch). Mesmo limite de 8 processos do estado.
    /// </summary>
    private static async Task LoadBaseDistancesAsync(
        IReadOnlyList<WorktreeRow> rows,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(8);

        var tasks = rows.Select(async row =>
        {
            if (row.Worktree.Branch is not { } branch || row.BaseBranch is not { } baseBranch || !row.CanLaunch)
            {
                row.BaseDistance = null;
                return;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                var remote = await GitService.ResolveRemoteAsync(row.FullPath, branch, cancellationToken).ConfigureAwait(true);
                var count = await GitService
                    .CountDistanceAsync(row.FullPath, $"{remote}/{baseBranch}", cancellationToken)
                    .ConfigureAwait(true);

                row.BaseDistance = count is { } known ? new BaseDistance(remote, baseBranch, known.Behind, known.Ahead) : null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A distância é enfeite do tooltip: sem ela, o ícone continua valendo.
                row.BaseDistance = null;
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

/// <summary>
/// Resultado de uma ação em lote, em três grupos: as linhas em que funcionou, as em que
/// falhou (com o motivo) e as que não suportam a ação e ficaram de fora.
/// </summary>
public sealed record BatchOutcome(
    string Action,
    IReadOnlyList<string> Succeeded,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> NotApplicable)
{
    public bool HasProblems => Failed.Count > 0 || NotApplicable.Count > 0;

    /// <summary>Uma linha para o rodapé.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Succeeded.Count > 0) parts.Add($"{Succeeded.Count} ok");
            if (Failed.Count > 0) parts.Add($"{Failed.Count} {(Failed.Count == 1 ? "falhou" : "falharam")} — ver detalhes");
            if (NotApplicable.Count > 0) parts.Add($"{NotApplicable.Count} não se {(NotApplicable.Count == 1 ? "aplica" : "aplicam")}");
            if (parts.Count == 0) parts.Add("nada a fazer");
            return $"{Action}: " + string.Join(" · ", parts);
        }
    }

    /// <summary>Texto do diálogo de relatório, no formato do de worktrees mantidos da limpeza.</summary>
    public string Report
    {
        get
        {
            var sections = new List<string>();
            if (Succeeded.Count > 0) sections.Add($"Funcionou ({Succeeded.Count}):\n\n" + string.Join("\n", Succeeded));
            if (Failed.Count > 0) sections.Add($"Falhou ({Failed.Count}):\n\n" + string.Join("\n\n", Failed));
            if (NotApplicable.Count > 0) sections.Add($"Não se aplica ({NotApplicable.Count}):\n\n" + string.Join("\n", NotApplicable));
            return string.Join("\n\n\n", sections);
        }
    }
}

/// <summary>Resultado da preparação: a distância medida depois do fetch, ou o motivo da recusa.</summary>
public sealed record BaseUpdatePlan(BaseDistance? Distance, string? Error)
{
    public static BaseUpdatePlan Refused(string error) => new(null, error);
}

/// <summary>Resumo para o rodapé e, quando há algo a explicar, o texto de um diálogo.</summary>
public sealed record BaseUpdateOutcome(bool Success, string Summary, string? Details);

internal static class OrderingExtensions
{
    public static IOrderedEnumerable<T> Then<T, TKey>(
        this IOrderedEnumerable<T> source,
        Func<T, TKey> key,
        bool descending)
        => descending ? source.ThenByDescending(key) : source.ThenBy(key);
}
