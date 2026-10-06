using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>Uma opção de filtro. <paramref name="Value"/> null é "todas"; <paramref name="IsNone"/>, "sem".</summary>
public sealed record FilterOption(string Label, string? Value, bool IsNone = false)
{
    public override string ToString() => Label;
}

/// <summary>Uma linha da lista por impacto.</summary>
public sealed record IssueImpactItem(
    IssueKey Key,
    int Number,
    string Title,
    string Url,
    int Reach,
    int OpenBlockers,
    bool IsInCycle,
    string? Color,
    string Meta,
    string? WorktreeTip = null)
{
    public string Label => $"#{Number}";

    /// <summary>Já tem worktree (#148): o menu da linha é o do worktree.</summary>
    public bool HasWorktree => WorktreeTip is not null;

    public bool IsFree => OpenBlockers == 0;

    public bool IsBlocked => !IsFree;

    public string State => IsFree ? "livre" : $"presa por {OpenBlockers}";

    public string ReachText => Reach switch
    {
        0 => "não destrava nenhuma",
        1 => "destrava 1",
        _ => $"destrava {Reach}",
    };

    public bool HasMeta => Meta.Length > 0;
}

/// <summary>Um cartão do grafo, já posicionado.</summary>
public sealed record GraphNodeItem(
    IssueKey Key,
    string Label,
    string Title,
    string Url,
    string Detail,
    string? Color,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsLocalOpen,
    bool IsFree,
    bool IsClosed,
    bool IsExternal,
    bool IsInCycle,
    bool IsFocused,
    string? WorktreeTip = null)
{
    public bool IsBlocked => IsLocalOpen && !IsFree;

    /// <summary>Já tem worktree (#148): o menu do cartão é o do worktree.</summary>
    public bool HasWorktree => WorktreeTip is not null;

    public bool IsStub => !IsLocalOpen;

    /// <summary>O worktree é deste repositório: issue de outro não ganha um aqui (#147).</summary>
    public bool CanCreateWorktree => !IsExternal;
}

/// <summary>O vão que uma aresta longa atravessa numa coluna do meio: entra à esquerda e sai à direita, na mesma altura.</summary>
public sealed record GraphWaypoint(double Left, double Right, double Y);

/// <summary>
/// Uma aresta, de quem bloqueia (saída pela direita) para quem é bloqueado (entrada pela
/// esquerda), passando pelos vãos de <paramref name="Via"/> quando pula colunas.
/// <paramref name="IsCycle"/>: as duas pontas na mesma coluna, o que só acontece dentro de um
/// ciclo. <paramref name="IsResolved"/>: quem bloqueava já fechou.
/// </summary>
public sealed record GraphEdgeItem(
    double X1,
    double Y1,
    double X2,
    double Y2,
    IReadOnlyList<GraphWaypoint> Via,
    bool IsCycle,
    bool IsResolved);

/// <summary>
/// O grafo de issues do repositório da aba da frente (#144): qual issue atacar agora. Uma
/// instância só, no painel lateral e na janela própria, como a fila do Actions. Lê o GitHub ao
/// aparecer — trocar de aba, abrir o painel, a janela voltar ao foco com a leitura velha — e no
/// Atualizar; não há laço: o grafo de dependências muda devagar e a leitura custa pouco.
/// Desligado nas configurações, não lê nada.
/// </summary>
public sealed class IssueGraphViewModel : ObservableObject
{
    public const double NodeWidth = 200;
    public const double NodeHeight = 58;
    public const double ColumnGap = 56;
    public const double RowGap = 14;

    /// <summary>A altura do vão reservado para uma aresta longa passar por uma coluna.</summary>
    public const double WaypointHeight = 10;
    public const double Margin = 16;

    /// <summary>Leitura mais velha que isso é relida quando o grafo volta à vista.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public const string NoDependenciesText =
        "Nenhuma issue aberta deste repositório tem dependência registrada. O grafo sai das relações Blocked by / Blocking do GitHub: na página da issue, em Relationships, Mark as blocked by ou Mark as blocking. Menção a #123 no texto não conta — é sinal fraco demais para virar aresta.";

    public const string ReachTip =
        "Alcance transitivo: quantas issues abertas, somando todos os níveis, dependem desta — direta ou indiretamente. Conta todas as abertas do repositório, não só as do filtro. Livre é a aberta sem nenhum bloqueador aberto: bloqueador já fechado não segura mais nada.";

    public const string CreateWorktreeText = "Criar worktree para esta issue";

    public const string CreateWorktreeExternalText = "Criar worktree: a issue é de outro repositório";

    private readonly Settings _settings;
    private readonly Action _save;
    private readonly Func<string, CancellationToken, Task<IssueGraphData>> _load;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _post;
    private readonly Dictionary<string, RepositoryState> _states = new(StringComparer.Ordinal);

    private RepositoryState? _state;
    private string? _repositoryName;
    private CancellationTokenSource? _loading;
    private bool _started;
    private bool _showsGraph;
    private bool _onlyFree;
    private bool _rebuilding;
    private bool _mainActive = true;
    private bool _windowActive;
    private IReadOnlyList<GraphEdgeItem> _edges = Array.Empty<GraphEdgeItem>();
    private double _graphWidth;
    private double _graphHeight;

    /// <summary>O que foi lido de um repositório e o que a pessoa escolheu nele: volta igual ao voltar à aba.</summary>
    private sealed class RepositoryState(string path)
    {
        public readonly string Path = path;
        public IssueGraphData? Data;
        public IssueGraph? Graph;
        public Dictionary<IssueKey, int> Reach = new();
        public HashSet<IssueKey> InCycle = new();
        public IReadOnlyList<IReadOnlyList<IssueKey>> Cycles = Array.Empty<IReadOnlyList<IssueKey>>();
        public string? Problem;
        public bool IsLoading;
        public string? Milestone;
        public bool MilestoneNone;
        public string? Label;
        public bool LabelNone;
        public string? Type;
        public bool TypeNone;
        public IssueKey? Focus;
    }

    public IssueGraphViewModel(
        Settings settings,
        Action save,
        Func<string, CancellationToken, Task<IssueGraphData>>? load = null,
        Func<DateTimeOffset>? clock = null,
        Action<Action>? post = null)
    {
        _settings = settings;
        _save = save;
        _load = load ?? IssueGraphService.LoadAsync;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _post = post ?? PostToCurrentContext;
    }

    /// <summary>Para a fila do thread de interface, se houver uma; sem, na hora.</summary>
    private static void PostToCurrentContext(Action action)
    {
        if (SynchronizationContext.Current is { } context) context.Post(_ => action(), null);
        else action();
    }

    // ── Ligado, e onde a view está ──────────────────────────────────────────

    /// <summary>A opção das configurações. Desligada, nada aparece e nada é lido.</summary>
    public bool IsEnabled => _settings.IssueGraphEnabled;

    /// <summary>O painel ao lado da lista. Persiste entre sessões, mesmo com o recurso desligado.</summary>
    public bool IsPanelOpen
    {
        get => _settings.IssueGraphPanelOpen;
        set
        {
            if (_settings.IssueGraphPanelOpen == value) return;
            _settings.IssueGraphPanelOpen = value;
            OnHostsChanged();
            RaisePropertyChanged();
        }
    }

    /// <summary>O painel está de fato na tela: aberto e com o recurso ligado.</summary>
    public bool IsPanelVisible => IsEnabled && IsPanelOpen;

    /// <summary>A janela própria. Quem a abre e fecha é a janela principal; aqui só o estado, que persiste.</summary>
    public bool IsWindowOpen
    {
        get => _settings.IssueGraphWindowOpen;
        set
        {
            if (_settings.IssueGraphWindowOpen == value) return;
            _settings.IssueGraphWindowOpen = value;
            if (!value) _windowActive = false;
            OnHostsChanged();
            RaisePropertyChanged();
        }
    }

    /// <summary>Alguma view à vista e o recurso ligado: só assim lê o GitHub.</summary>
    public bool IsShown => IsEnabled && (IsPanelOpen || IsWindowOpen);

    private void OnHostsChanged()
    {
        _save();
        RaisePropertyChanged(nameof(IsPanelVisible));
        RaisePropertyChanged(nameof(IsShown));

        if (IsShown) _ = EnsureLoadedAsync();
        else CancelLoading();
    }

    /// <summary>A janela principal ganhou ou perdeu o foco, ou foi minimizada.</summary>
    public void SetMainWindowActivity(bool isActive, bool isMinimized)
    {
        var resumed = isActive && !_mainActive;
        _mainActive = isActive;
        if (resumed && !isMinimized && IsPanelOpen) _ = EnsureLoadedAsync();
    }

    /// <summary>A janela própria ganhou ou perdeu o foco, ou foi minimizada.</summary>
    public void SetDetachedWindowActivity(bool isActive, bool isMinimized)
    {
        var resumed = isActive && !_windowActive;
        _windowActive = isActive;
        if (resumed && !isMinimized) _ = EnsureLoadedAsync();
    }

    /// <summary>A tela de configurações mudou algo: o recurso pode ter sido ligado ou desligado.</summary>
    public void RefreshSettings()
    {
        RaisePropertyChanged(nameof(IsEnabled));
        RaisePropertyChanged(nameof(IsPanelVisible));
        RaisePropertyChanged(nameof(IsShown));

        if (IsShown) _ = EnsureLoadedAsync();
        else CancelLoading();
    }

    /// <summary>A janela abriu: a partir daqui, aparecer lê o GitHub. Antes, o construtor não chama nada.</summary>
    public void Start()
    {
        _started = true;
        _ = EnsureLoadedAsync();
    }

    // ── Worktree ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pedido de worktree para uma issue deste repositório, pelo menu do grafo (#147). Quem monta
    /// o diálogo é a janela principal, a mesma montagem do Novo worktree.
    /// </summary>
    public event Action<int>? WorktreeRequested;

    public void RequestWorktree(int number) => WorktreeRequested?.Invoke(number);

    // ── Repositório ─────────────────────────────────────────────────────────

    /// <summary>A aba da frente mudou: o grafo dela, ligado aos worktrees dela.</summary>
    public void SetRepository(RepositoryViewModel? repository)
    {
        WatchWorktrees(repository);
        SetRepository(repository?.RepositoryPath, repository?.DisplayName);
        RelinkWorktrees();
    }

    /// <summary>A aba da frente mudou. O grafo é sempre do repositório selecionado.</summary>
    public void SetRepository(string? path, string? name)
    {
        var next = path is null ? null : State(path);
        if (ReferenceEquals(next, _state)) return;

        CancelLoading();
        _state = next;
        _repositoryName = name;
        RaisePropertyChanged(nameof(RepositoryName));
        RaisePropertyChanged(nameof(HasRepository));
        RaisePropertyChanged(nameof(HasNoRepository));
        Rebuild();

        _ = EnsureLoadedAsync();
    }

    private RepositoryState State(string path)
    {
        if (!_states.TryGetValue(path, out var state)) _states[path] = state = new RepositoryState(path);
        return state;
    }

    public string? RepositoryName => _repositoryName;

    public bool HasRepository => _state is not null;

    public bool HasNoRepository => _state is null;

    // ── Worktrees (#148) ────────────────────────────────────────────────────

    private RepositoryViewModel? _worktreeSource;
    private readonly List<WorktreeRow> _watchedRows = new();
    private IReadOnlyDictionary<IssueKey, IssueWorktreeLink> _links = new Dictionary<IssueKey, IssueWorktreeLink>();
    private bool _relinkPending;

    /// <summary>O repositório da aba cujos worktrees o grafo marca; é nele que o menu do worktree age.</summary>
    public RepositoryViewModel? Repository => _worktreeSource;

    /// <summary>O worktree que já trata a issue, se algum — lido na hora, para o menu agir na linha atual da lista.</summary>
    public IssueWorktreeLink? WorktreeFor(IssueKey key) => _links.GetValueOrDefault(key);

    private void WatchWorktrees(RepositoryViewModel? repository)
    {
        if (ReferenceEquals(repository, _worktreeSource)) return;

        if (_worktreeSource is { } previous) previous.Worktrees.CollectionChanged -= OnWorktreesChanged;
        WatchRows(Array.Empty<WorktreeRow>());

        _worktreeSource = repository;
        if (repository is not null)
        {
            repository.Worktrees.CollectionChanged += OnWorktreesChanged;
            WatchRows(repository.Worktrees);
        }

        RaisePropertyChanged(nameof(Repository));
    }

    /// <summary>O PR de uma linha muda depois dela entrar na lista: é ele que diz a issue declarada.</summary>
    private void WatchRows(IEnumerable<WorktreeRow> rows)
    {
        foreach (var row in _watchedRows) row.PropertyChanged -= OnRowChanged;
        _watchedRows.Clear();
        _watchedRows.AddRange(rows);
        foreach (var row in _watchedRows) row.PropertyChanged += OnRowChanged;
    }

    private void OnWorktreesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_worktreeSource is { } repository) WatchRows(repository.Worktrees);
        ScheduleRelink();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorktreeRow.PullRequest)) ScheduleRelink();
    }

    /// <summary>
    /// A lista se refaz linha a linha (limpa e adiciona) e os PRs chegam um por um: junta tudo
    /// numa religação só, depois. Remover um worktree pelo grafo desmarca a issue na hora.
    /// </summary>
    private void ScheduleRelink()
    {
        if (_relinkPending) return;
        _relinkPending = true;
        _post(() =>
        {
            _relinkPending = false;
            RelinkWorktrees();
        });
    }

    /// <summary>Refaz as ligações; só remonta lista e grafo se alguma marca mudou.</summary>
    private void RelinkWorktrees()
    {
        var links = _state?.Data?.Repository is { } repository
            ? BuildLinks(repository)
            : new Dictionary<IssueKey, IssueWorktreeLink>();

        var changed = links.Count != _links.Count
                      || links.Any(pair => _links.GetValueOrDefault(pair.Key)?.Tooltip != pair.Value.Tooltip);
        _links = links;
        if (changed) Rebuild();
    }

    // ── Leitura ─────────────────────────────────────────────────────────────

    /// <summary>A leitura em curso, para quem precisa esperar por ela (os testes).</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Lê se ainda não leu, ou se a leitura passou de <see cref="StaleAfter"/>.</summary>
    public Task EnsureLoadedAsync()
    {
        if (_state is not { } state || state.IsLoading) return Loading;
        if (state.Data is { } data && _clock() - data.ReadAt < StaleAfter) return Loading;
        return LoadAsync();
    }

    /// <summary>Atualizar: lê agora.</summary>
    public Task RefreshNowAsync() => _state is { IsLoading: false } ? LoadAsync() : Loading;

    private Task LoadAsync()
    {
        if (!_started || !IsShown || _state is not { } state) return Loading;
        Loading = LoadCoreAsync(state);
        return Loading;
    }

    private async Task LoadCoreAsync(RepositoryState state)
    {
        CancelLoading();
        var loading = new CancellationTokenSource();
        _loading = loading;
        state.IsLoading = true;
        RaiseStatus();

        try
        {
            var data = await _load(state.Path, loading.Token).ConfigureAwait(true);
            if (loading.IsCancellationRequested) return;

            state.Data = data with { ReadAt = _clock() };
            state.Problem = null;
            Analyze(state);
            if (ReferenceEquals(state, _state)) _links = BuildLinks(data.Repository);
        }
        catch (OperationCanceledException)
        {
            // Trocou de aba ou recolheu no meio: a próxima vez que aparecer, lê de novo.
        }
        catch (Exception exception)
        {
            if (!loading.IsCancellationRequested) state.Problem = exception.Message;
        }
        finally
        {
            state.IsLoading = false;
            if (ReferenceEquals(_loading, loading)) _loading = null;
            loading.Dispose();
        }

        if (ReferenceEquals(state, _state)) Rebuild();
    }

    private void CancelLoading()
    {
        _loading?.Cancel();
        _loading = null;
    }

    /// <summary>O que não muda com filtro nem foco: o grafo inteiro, o alcance de cada issue e os ciclos.</summary>
    private static void Analyze(RepositoryState state)
    {
        var data = state.Data!;
        var graph = IssueGraph.Build(data.Repository, data.Issues);
        state.Graph = graph;
        state.Reach = data.Issues.ToDictionary(issue => issue.Key, issue => graph.Reach(issue.Key).Count);
        state.Cycles = graph.Cycles();
        state.InCycle = state.Cycles.SelectMany(cycle => cycle).ToHashSet();
        if (state.Focus is { } focus && !graph.Nodes.ContainsKey(focus)) state.Focus = null;
    }

    private IReadOnlyDictionary<IssueKey, IssueWorktreeLink> BuildLinks(string repository)
        => _worktreeSource is { } source
            ? IssueWorktreeLink.Build(repository, source.Worktrees)
            : new Dictionary<IssueKey, IssueWorktreeLink>();

    public bool IsLoading => _state?.IsLoading == true;

    public bool HasData => _state?.Data is not null;

    public string? Problem => _state?.Problem;

    public bool HasProblem => Problem is not null;

    public string StatusLine
    {
        get
        {
            if (_state is not { } state) return string.Empty;
            if (state.IsLoading && state.Data is null) return "Lendo as issues do GitHub…";

            var parts = new List<string>();
            if (state.Data is { } data)
            {
                parts.Add($"Atualizado às {data.ReadAt.ToLocalTime():HH:mm:ss}");
                parts.Add(data.Issues.Count == 1 ? "1 aberta" : $"{data.Issues.Count} abertas");
                var edges = state.Graph?.EdgeCount ?? 0;
                parts.Add(edges == 1 ? "1 dependência" : $"{edges} dependências");
            }
            if (state.IsLoading) parts.Add("lendo…");
            return string.Join(" · ", parts);
        }
    }

    public string? TruncatedNotice => _state?.Data is { Truncated: true } data
        ? $"Só as {data.Issues.Count} issues abertas mais antigas: o repositório tem mais do que a leitura traz."
        : null;

    public bool IsTruncated => TruncatedNotice is not null;

    // ── Filtros ─────────────────────────────────────────────────────────────

    public ObservableCollection<FilterOption> MilestoneOptions { get; } = new();

    public ObservableCollection<FilterOption> LabelOptions { get; } = new();

    public ObservableCollection<FilterOption> TypeOptions { get; } = new();

    private FilterOption? _milestone;
    private FilterOption? _label;
    private FilterOption? _type;

    public FilterOption? SelectedMilestone
    {
        get => _milestone;
        set => SetFilter(ref _milestone, value, (state, option) => (state.Milestone, state.MilestoneNone) = (option.Value, option.IsNone));
    }

    public FilterOption? SelectedLabel
    {
        get => _label;
        set => SetFilter(ref _label, value, (state, option) => (state.Label, state.LabelNone) = (option.Value, option.IsNone));
    }

    public FilterOption? SelectedType
    {
        get => _type;
        set => SetFilter(ref _type, value, (state, option) => (state.Type, state.TypeNone) = (option.Value, option.IsNone));
    }

    /// <summary>O repositório tem tipos de issue: a organização configurou. Conta pessoal não tem, e o filtro some.</summary>
    public bool HasTypes => TypeOptions.Count > 1;

    private void SetFilter(ref FilterOption? field, FilterOption? value, Action<RepositoryState, FilterOption> store)
    {
        // O ComboBox manda null enquanto a lista é refeita.
        if (value is null || _rebuilding || value == field) return;
        field = value;
        if (_state is { } state) store(state, value);
        Rebuild();
    }

    /// <summary>Só as livres: aberta, sem bloqueador aberto.</summary>
    public bool OnlyFree
    {
        get => _onlyFree;
        set
        {
            if (!SetProperty(ref _onlyFree, value)) return;
            Rebuild();
        }
    }

    /// <summary>
    /// Desenha as fechadas que chegam como ponta de dependência (#146). Só exibição: a consulta
    /// não muda, e livre e alcance já contam só bloqueadores abertos, marcado ou não. Persiste.
    /// </summary>
    public bool ShowClosed
    {
        get => _settings.IssueGraphShowClosed;
        set
        {
            if (_settings.IssueGraphShowClosed == value) return;
            _settings.IssueGraphShowClosed = value;
            _save();
            RaisePropertyChanged();

            // O foco numa fechada que acabou de sumir não tem o que mostrar.
            if (!value && _state is { Focus: { } focus, Graph: { } graph } state && !graph.Nodes[focus].IsOpen) state.Focus = null;
            Rebuild();
        }
    }

    // ── Lista e grafo ───────────────────────────────────────────────────────

    /// <summary>O grafo à vista; senão, a lista por impacto. A lista decide mais rápido; o grafo explica.</summary>
    public bool ShowsGraph
    {
        get => _showsGraph;
        set
        {
            if (!SetProperty(ref _showsGraph, value)) return;
            RaisePropertyChanged(nameof(ShowsList));
        }
    }

    public bool ShowsList => !_showsGraph;

    public ObservableCollection<IssueImpactItem> Items { get; } = new();

    public ObservableCollection<GraphNodeItem> GraphNodes { get; } = new();

    public IReadOnlyList<GraphEdgeItem> GraphEdges
    {
        get => _edges;
        private set => SetProperty(ref _edges, value);
    }

    public double GraphWidth
    {
        get => _graphWidth;
        private set => SetProperty(ref _graphWidth, value);
    }

    public double GraphHeight
    {
        get => _graphHeight;
        private set => SetProperty(ref _graphHeight, value);
    }

    /// <summary>Leu, e nenhuma issue aberta do repositório tem dependência: o grafo daria uma tela vazia sem explicação.</summary>
    public bool HasNoDependencies => _state is { Data: not null, Graph.HasEdges: false };

    public bool HasDependencies => _state is { Graph.HasEdges: true };

    public bool HasNoItems => _state?.Data is not null && Items.Count == 0;

    private string? _graphNote;

    /// <summary>O que o grafo deixou de fora: as issues sem dependência só aparecem na lista.</summary>
    public string? GraphNote
    {
        get => _graphNote;
        private set => SetProperty(ref _graphNote, value);
    }

    public string? CycleWarning => _state is { Cycles.Count: > 0 } state
        ? "Ciclo de dependência — nenhuma destas fica livre até alguém desfazer o ciclo: "
          + string.Join("; ", state.Cycles.Select(cycle => string.Join(" ↔ ", cycle.Select(Name))))
        : null;

    public bool HasCycles => CycleWarning is not null;

    // ── Foco ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Isola uma issue: ela, tudo o que a segura e tudo o que ela destrava, em todos os níveis,
    /// mais as pontas diretas fechadas ou de fora. Com centenas de abertas, o grafo inteiro vira teia.
    /// O foco ignora os filtros: é a vizinhança inteira da issue.
    /// </summary>
    public void Focus(IssueKey key)
    {
        if (_state is not { Graph: { } graph } state || !graph.Nodes.TryGetValue(key, out var node) || !Visible(node)) return;
        if (state.Focus == key) return;
        state.Focus = key;
        Rebuild();
    }

    public void ClearFocus()
    {
        if (_state is not { Focus: not null } state) return;
        state.Focus = null;
        Rebuild();
    }

    public bool IsFocused => _state?.Focus is not null;

    public string? FocusTitle => _state is { Focus: { } focus, Graph: { } graph } && graph.Nodes.TryGetValue(focus, out var node)
        ? $"Foco em #{node.Number} — o que a segura e o que ela destrava"
        : null;

    // ── Montagem ────────────────────────────────────────────────────────────

    /// <summary>#123 para a daqui; owner/repo#123, com a caixa do GitHub, para a de fora.</summary>
    private string Name(IssueKey key)
    {
        var local = _state?.Data?.Repository is { } repository && string.Equals(repository, key.Repository, StringComparison.OrdinalIgnoreCase);
        if (local) return $"#{key.Number}";
        return _state?.Graph?.Nodes.TryGetValue(key, out var node) == true ? $"{node.Repository}#{key.Number}" : key.ToString();
    }

    private static bool Matches(RepositoryState state, IssueData issue)
        => (state.MilestoneNone ? issue.Milestone is null : state.Milestone is null || issue.Milestone == state.Milestone)
           && (state.LabelNone ? issue.Labels.Count == 0 : state.Label is null || issue.Labels.Any(label => label.Name == state.Label))
           && (state.TypeNone ? issue.Type is null : state.Type is null || issue.Type == state.Type);

    /// <summary>Refaz filtros, lista e grafo a partir da leitura que está na tela.</summary>
    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            RebuildFilters();
        }
        finally
        {
            _rebuilding = false;
        }

        RebuildItems();
        RebuildGraph();

        foreach (var property in new[]
                 {
                     nameof(SelectedMilestone), nameof(SelectedLabel), nameof(SelectedType), nameof(HasTypes),
                     nameof(HasNoItems), nameof(HasNoDependencies), nameof(HasDependencies), nameof(CycleWarning), nameof(HasCycles),
                     nameof(IsFocused), nameof(FocusTitle), nameof(TruncatedNotice), nameof(IsTruncated),
                 })
            RaisePropertyChanged(property);

        RaiseStatus();
    }

    private void RaiseStatus()
    {
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(HasData));
        RaisePropertyChanged(nameof(Problem));
        RaisePropertyChanged(nameof(HasProblem));
        RaisePropertyChanged(nameof(StatusLine));
    }

    private void RebuildFilters()
    {
        var issues = _state?.Data?.Issues ?? Array.Empty<IssueData>();

        Fill(MilestoneOptions, "Todas as milestones", "Sem milestone",
            issues.Select(issue => issue.Milestone).OfType<string>(), issues.Any(issue => issue.Milestone is null));
        Fill(LabelOptions, "Todas as labels", "Sem label",
            issues.SelectMany(issue => issue.Labels.Select(label => label.Name)), issues.Any(issue => issue.Labels.Count == 0));

        // Os tipos da organização, mais algum que só apareça nas issues. Nenhum: o filtro some.
        var types = (_state?.Data?.Types ?? Array.Empty<string>()).Concat(issues.Select(issue => issue.Type).OfType<string>()).ToList();
        if (types.Count == 0) TypeOptions.Clear();
        else Fill(TypeOptions, "Todos os tipos", "Sem tipo", types, issues.Any(issue => issue.Type is null));

        var state = _state;
        _milestone = Pick(MilestoneOptions, state?.Milestone, state?.MilestoneNone == true);
        _label = Pick(LabelOptions, state?.Label, state?.LabelNone == true);
        _type = Pick(TypeOptions, state?.Type, state?.TypeNone == true);

        // A escolha sumiu da leitura nova: volta a "todas", e o estado também.
        if (state is not null)
        {
            (state.Milestone, state.MilestoneNone) = (_milestone?.Value, _milestone?.IsNone == true);
            (state.Label, state.LabelNone) = (_label?.Value, _label?.IsNone == true);
            (state.Type, state.TypeNone) = (_type?.Value, _type?.IsNone == true);
        }
    }

    private static void Fill(ObservableCollection<FilterOption> options, string all, string none, IEnumerable<string> values, bool hasNone)
    {
        var next = new List<FilterOption> { new(all, null) };
        next.AddRange(values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Select(value => new FilterOption(value, value)));
        if (hasNone) next.Add(new FilterOption(none, null, IsNone: true));

        if (next.SequenceEqual(options)) return;
        options.Clear();
        foreach (var option in next) options.Add(option);
    }

    private static FilterOption? Pick(IEnumerable<FilterOption> options, string? value, bool none)
    {
        var list = options.ToList();
        return list.FirstOrDefault(option => none ? option.IsNone : !option.IsNone && option.Value == value) ?? list.FirstOrDefault();
    }

    private void RebuildItems()
    {
        Items.Clear();
        if (_state is not { Data: { } data, Graph: { } graph } state) return;

        var items = data.Issues
            .Where(issue => Matches(state, issue))
            .Select(issue => new IssueImpactItem(
                issue.Key,
                issue.Number,
                issue.Title,
                issue.Url,
                state.Reach.GetValueOrDefault(issue.Key),
                graph.OpenBlockers(issue.Key),
                state.InCycle.Contains(issue.Key),
                Color(issue),
                Meta(issue),
                WorktreeFor(issue.Key)?.Tooltip))
            .Where(item => !_onlyFree || item.IsFree)
            .OrderByDescending(item => item.Reach)
            .ThenBy(item => item.OpenBlockers)
            .ThenBy(item => item.Number);

        foreach (var item in items) Items.Add(item);
    }

    private static string? Color(IssueData issue) => issue.TypeColor ?? issue.Labels.Select(label => label.Color).OfType<string>().FirstOrDefault();

    private static string Meta(IssueData issue)
        => string.Join(" · ", new[] { issue.Type, issue.Milestone, issue.Labels.Count == 0 ? null : string.Join(", ", issue.Labels.Select(label => label.Name)) }
            .OfType<string>());

    private void RebuildGraph()
    {
        GraphNodes.Clear();

        if (_state is not { Data: { } data, Graph: { } graph } state)
        {
            GraphEdges = Array.Empty<GraphEdgeItem>();
            GraphWidth = GraphHeight = 0;
            GraphNote = null;
            return;
        }

        IssueGraph shown;
        var showClosed = ShowClosed;
        if (state.Focus is { } focus)
        {
            var keep = new HashSet<IssueKey> { focus };
            keep.UnionWith(graph.Upstream(focus));
            keep.UnionWith(graph.Reach(focus));
            keep.UnionWith(graph.Predecessors(focus));
            keep.UnionWith(graph.Successors(focus));
            keep.RemoveWhere(key => key != focus && !Visible(graph.Nodes[key]));
            shown = graph.Subgraph(keep);
            GraphNote = null;
        }
        else
        {
            // Os filtros valem antes do layout. As pontas fechadas ou de fora vêm junto das
            // issues que ficaram — as fechadas só se a pessoa pediu; issue sem aresta nenhuma
            // não entra — fica só na lista. A ponta que sai leva junto as arestas dela.
            var matching = data.Issues.Where(issue => Matches(state, issue)).Select(issue => issue.Key).ToHashSet();
            var keep = new HashSet<IssueKey>(matching);
            bool Stub(IssueKey other) => graph.Nodes[other] is { IsLocalOpen: false } node && Visible(node);
            foreach (var key in matching)
            {
                keep.UnionWith(graph.Predecessors(key).Where(Stub));
                keep.UnionWith(graph.Successors(key).Where(Stub));
            }

            var filtered = graph.Subgraph(keep);
            var connected = filtered.Edges.SelectMany(edge => new[] { edge.From, edge.To }).ToHashSet();
            shown = filtered.Subgraph(connected);

            var alone = matching.Count(key => !connected.Contains(key));
            var dependency = showClosed ? "dependência" : "dependência aberta";
            GraphNote = !graph.HasEdges ? null
                : connected.Count == 0 ? $"Nenhuma issue do filtro tem {dependency} — estão todas só na lista."
                : alone == 0 ? null
                : alone == 1 ? $"1 issue sem {dependency} fica só na lista."
                : $"{alone} issues sem {dependency} ficam só na lista.";
        }

        var layout = IssueLayout.Compute(shown);
        double Slot(IssueKey key) => IssueLayout.IsWaypoint(key) ? WaypointHeight : NodeHeight;
        double Height(IReadOnlyList<IssueKey> layer) => layer.Count == 0 ? 0 : layer.Sum(Slot) + (layer.Count - 1) * RowGap;

        var tallest = layout.Layers.Count == 0 ? 0 : layout.Layers.Max(Height);
        var positions = new Dictionary<IssueKey, (double X, double Y, int Layer)>();

        for (var column = 0; column < layout.Layers.Count; column++)
        {
            var layer = layout.Layers[column];
            var x = Margin + column * (NodeWidth + ColumnGap);
            // Colunas mais curtas ficam centradas na altura da mais alta.
            var y = Margin + (tallest - Height(layer)) / 2;
            foreach (var key in layer)
            {
                positions[key] = (x, y, column);
                y += Slot(key) + RowGap;
            }
        }

        foreach (var (key, (x, y, _)) in positions.Where(item => !IssueLayout.IsWaypoint(item.Key))
                     .OrderBy(item => item.Value.Layer).ThenBy(item => item.Value.Y))
        {
            var node = shown.Nodes[key];
            GraphNodes.Add(new GraphNodeItem(
                key,
                Name(key),
                node.Title,
                node.Url,
                Detail(state, graph, node),
                node.Data is { } issue ? Color(issue) : null,
                x,
                y,
                NodeWidth,
                NodeHeight,
                node.IsLocalOpen,
                node.IsLocalOpen && graph.IsFree(key),
                !node.IsOpen,
                node.IsExternal,
                state.InCycle.Contains(key),
                state.Focus == key,
                node.IsLocalOpen ? WorktreeFor(key)?.Tooltip : null));
        }

        GraphEdges = shown.Edges
            .Select(edge =>
            {
                var from = positions[edge.From];
                var to = positions[edge.To];
                var cycle = from.Layer == to.Layer;
                var via = layout.Route(edge.From, edge.To)
                    .Select(waypoint => positions[waypoint])
                    .Select(point => new GraphWaypoint(point.X, point.X + NodeWidth, point.Y + WaypointHeight / 2))
                    .ToList();
                return new GraphEdgeItem(
                    from.X + NodeWidth, from.Y + NodeHeight / 2,
                    cycle ? to.X + NodeWidth : to.X, to.Y + NodeHeight / 2,
                    via,
                    cycle,
                    !shown.Nodes[edge.From].IsOpen);
            })
            .ToList();

        GraphWidth = layout.Layers.Count == 0 ? 0 : 2 * Margin + layout.Layers.Count * NodeWidth + (layout.Layers.Count - 1) * ColumnGap
                                                    + (GraphEdges.Any(edge => edge.IsCycle) ? ColumnGap : 0);
        GraphHeight = tallest == 0 ? 0 : 2 * Margin + tallest;
    }

    /// <summary>A fechada só entra no desenho com <see cref="ShowClosed"/>; o resto, sempre.</summary>
    private bool Visible(IssueGraphNode node) => node.IsOpen || ShowClosed;

    /// <summary>A segunda linha do cartão: o estado para a aberta daqui; fechada ou de fora para as pontas.</summary>
    private static string Detail(RepositoryState state, IssueGraph graph, IssueGraphNode node)
    {
        if (!node.IsOpen) return node.IsExternal ? $"{node.Repository} · fechada" : "fechada";
        if (!node.IsLocalOpen) return $"fora deste repositório · {node.Repository}";

        var blockers = graph.OpenBlockers(node.Key);
        var reach = state.Reach.GetValueOrDefault(node.Key);
        var status = blockers == 0 ? "livre" : $"presa por {blockers}";
        return reach == 0 ? status : $"{status} · destrava {reach}";
    }
}
