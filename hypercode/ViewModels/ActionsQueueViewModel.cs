using System.Collections.ObjectModel;
using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>Estado de um runner, do mais ao menos interessante — é a ordem da lista.</summary>
public enum RunnerState { Busy, Idle, Offline }

/// <summary>
/// O run sobre o qual um item do painel age (#133). O GitHub só cancela run inteiro — não há
/// cancelar job —, então job na fila e runner ocupado apontam para o run deles. Os motivos de
/// bloqueio dizem por que o item do menu sai desabilitado; null é que dá.
/// </summary>
public sealed record RunTarget(
    string Repository,
    long Id,
    string Workflow,
    string Title,
    int? Number,
    string? Branch,
    int? PullRequest,
    string? Url,
    string? CancelBlocked,
    string? ForceCancelBlocked)
{
    public bool CanCancel => CancelBlocked is null;

    public bool CanForceCancel => ForceCancelBlocked is null;

    /// <summary>O cancelamento já foi pedido e o GitHub ainda não parou o run.</summary>
    public bool IsCancelRequested { get; init; }

    /// <summary>CI #482: o workflow e o número do run nele.</summary>
    public string Name => Number is { } number ? $"{Workflow} #{number}" : Workflow;

    /// <summary>PR #12 (feat/x), ou a branch sem PR.</summary>
    public string? Ref => PullRequest is { } number
        ? Branch is null ? $"PR #{number}" : $"PR #{number} ({Branch})"
        : Branch;
}

public sealed record RunnerItem(string Name, string Detail, RunnerState State, string? Job, string? JobOrigin, RunTarget? Run = null)
{
    public string StateLabel => State switch
    {
        RunnerState.Busy => "ocupado",
        RunnerState.Idle => "livre",
        _ => "offline",
    };

    public bool IsBusy => State == RunnerState.Busy;
    public bool IsIdle => State == RunnerState.Idle;
    public bool IsOffline => State == RunnerState.Offline;
    public bool HasJob => Job is not null;

    public bool IsCancelRequested => Run?.IsCancelRequested == true;
}

public sealed record RunnerGroupItem(string Name, string Summary, IReadOnlyList<RunnerItem> Runners);

public sealed record QueuedJobItem(string Position, string Title, string Origin, string Wait, string Estimate, RunTarget? Run = null)
{
    public bool IsCancelRequested => Run?.IsCancelRequested == true;
}

public sealed record LaneItem(string Labels, string Summary, IReadOnlyList<QueuedJobItem> Jobs);

public sealed record WaitingRunItem(string Title, string Origin, string Wait, RunTarget? Run = null)
{
    public bool IsCancelRequested => Run?.IsCancelRequested == true;
}

/// <summary>
/// Um run (ou job) segurado por <c>concurrency</c> (#131). <see cref="Holder"/> é quem roda no
/// grupo — o que destrava —, com o link dele; o menu age sobre o segurado, como nos outros itens.
/// </summary>
public sealed record HeldRunItem(string Title, string Origin, string Wait, string Group, string Holder, string? HolderUrl, RunTarget? Run = null)
{
    public bool IsCancelRequested => Run?.IsCancelRequested == true;

    public bool HasHolderUrl => HolderUrl is not null;

    public string Unlock => $"destrava quando {Holder} terminar ou for cancelado";
}

/// <summary>
/// A fila do GitHub Actions (#130): os runners e o que espera por eles, nos repositórios
/// escolhidos nas configurações. Uma instância só, hospedada no painel lateral da janela e na
/// janela própria — as duas views mostram o mesmo estado. Com as duas fechadas, não busca nada.
/// A cadência é a do <see cref="MonitorScheduler"/>: perfil global, janela e recuo pela cota REST.
/// </summary>
public sealed class ActionsQueueViewModel : ObservableObject, IDisposable
{
    /// <summary>Quanto o laço espera entre uma pergunta e outra ao agendador — só conta em memória.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    public const string EstimateNotice =
        "Ordem estimada pelo horário de entrada na fila. O GitHub não expõe a posição: o despacho para self-hosted é FIFO na prática, mas não é contrato — e jobs de repositórios não acompanhados disputam os mesmos runners sem aparecer aqui.";

    public const string EstimateTip =
        "Aproximação: soma a duração mediana de cada job, nos últimos runs concluídos do workflow dele, ao que já está rodando nos runners que atendem às labels. Runner ocupado com job sem histórico, ou de repositório não acompanhado, fica fora da conta — por isso ela tende a errar para mais, não para menos.";

    public const string NoEstimate = "sem estimativa";

    /// <summary>Depois de uma coleta que falhou (ou que não deu em nada), o mínimo até a próxima tentativa.</summary>
    private static readonly TimeSpan HistoryRetry = TimeSpan.FromMinutes(15);

    private readonly Settings _settings;
    private readonly Action _save;
    private readonly Func<string, CancellationToken, Task<ApiResponse>> _get;
    private readonly Func<string, CancellationToken, Task<ApiResponse>> _post;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<Task> _settle;
    private readonly MonitorScheduler _scheduler = new();
    private readonly ActionsDurationStore? _durationStore;
    private readonly Dictionary<string, RepositoryDurations> _durationsByRepository;
    private ActionsDurations _durations;
    private Task _history = Task.CompletedTask;
    private bool _isCollecting;
    private DateTimeOffset _historyAttempt = DateTimeOffset.MinValue;

    private CancellationTokenSource? _loop;
    private CancellationTokenSource? _load;
    private ActionsSnapshot? _snapshot;
    private bool _isLoading;
    private string? _loadProblem;

    /// <summary>Pedido de releitura que chegou com uma leitura em curso: ela já pode estar velha.</summary>
    private bool _rereadPending;

    /// <summary>Se dá para cancelar, por repositório. Lido uma vez; sai daqui quando o GitHub recusa por permissão.</summary>
    private readonly Dictionary<string, WriteAccess> _access = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs com cancelamento pedido que ainda aparecem na leitura: o GitHub leva uns segundos para pará-los.</summary>
    private readonly HashSet<(string Repository, long Id)> _cancelRequested = new(RunKeyComparer.Instance);

    private bool _mainActive = true;
    private bool _mainMinimized;
    private bool _windowActive;
    private bool _windowMinimized;

    public ActionsQueueViewModel(
        Settings settings,
        Action save,
        Func<string, CancellationToken, Task<ApiResponse>>? get = null,
        Func<DateTimeOffset>? clock = null,
        Func<string, CancellationToken, Task<ApiResponse>>? post = null,
        Func<Task>? settle = null,
        ActionsDurationStore? durations = null)
    {
        _settings = settings;
        _save = save;
        _get = get ?? ActionsQueueService.GetAsync;
        _post = post ?? ActionsQueueService.PostAsync;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _settle = settle ?? (() => Task.Delay(ActionSettleDelay));
        _durationStore = durations;
        _durationsByRepository = durations?.Load() ?? new Dictionary<string, RepositoryDurations>(StringComparer.OrdinalIgnoreCase);
        _durations = TrackedDurations();
        _scheduler.Profile = EffectiveSettings.Resolve(settings, null).MonitorProfile;
        UpdateSchedulerActivity();
    }

    // ── Onde a view está ────────────────────────────────────────────────────

    /// <summary>O painel ao lado da lista de worktrees. Persiste entre sessões.</summary>
    public bool IsPanelOpen
    {
        get => _settings.ActionsPanelOpen;
        set
        {
            if (_settings.ActionsPanelOpen == value) return;
            _settings.ActionsPanelOpen = value;
            OnHostsChanged();
            RaisePropertyChanged();
        }
    }

    /// <summary>A janela própria. Quem a abre e fecha é a janela principal; aqui só o estado, que persiste.</summary>
    public bool IsWindowOpen
    {
        get => _settings.ActionsWindowOpen;
        set
        {
            if (_settings.ActionsWindowOpen == value) return;
            _settings.ActionsWindowOpen = value;
            if (!value) _windowActive = _windowMinimized = false;
            OnHostsChanged();
            RaisePropertyChanged();
        }
    }

    /// <summary>Alguma view está à vista: só assim o laço busca alguma coisa.</summary>
    public bool IsShown => IsPanelOpen || IsWindowOpen;

    private void OnHostsChanged()
    {
        _save();
        UpdateSchedulerActivity();
        RaisePropertyChanged(nameof(IsShown));

        if (IsShown)
        {
            _scheduler.ExpireActions();
            _ = CheckAsync();
        }
        else
        {
            _load?.Cancel();
        }
    }

    /// <summary>A janela principal ganhou ou perdeu o foco, ou foi minimizada.</summary>
    public void SetMainWindowActivity(bool isActive, bool isMinimized)
    {
        _mainActive = isActive;
        _mainMinimized = isMinimized;
        UpdateSchedulerActivity();
        if (isActive && !isMinimized && IsPanelOpen) _ = CheckAsync();
    }

    /// <summary>A janela própria ganhou ou perdeu o foco, ou foi minimizada.</summary>
    public void SetDetachedWindowActivity(bool isActive, bool isMinimized)
    {
        _windowActive = isActive;
        _windowMinimized = isMinimized;
        UpdateSchedulerActivity();
        if (isActive && !isMinimized) _ = CheckAsync();
    }

    /// <summary>
    /// Para o agendador, ativa é a view que está numa janela em foco; minimizada, só se todas
    /// as que mostram a fila estiverem. A janela própria num segundo monitor segue valendo
    /// mesmo com a principal minimizada.
    /// </summary>
    private void UpdateSchedulerActivity()
    {
        _scheduler.IsWindowActive = (IsPanelOpen && _mainActive) || (IsWindowOpen && _windowActive);
        _scheduler.IsWindowMinimized = !((IsPanelOpen && !_mainMinimized) || (IsWindowOpen && !_windowMinimized));
    }

    // ── Configuração ────────────────────────────────────────────────────────

    public IReadOnlyList<string> Repositories => _settings.ActionsRepositories;

    public bool HasRepositories => _settings.ActionsRepositories.Count > 0;

    public bool HasNoRepositories => !HasRepositories;

    public string RepositoriesSummary => HasRepositories
        ? string.Join(", ", _settings.ActionsRepositories)
        : string.Empty;

    /// <summary>A tela de configurações mudou algo: perfil global e lista de repositórios.</summary>
    public void RefreshSettings()
    {
        _scheduler.Profile = EffectiveSettings.Resolve(_settings, null).MonitorProfile;

        RaisePropertyChanged(nameof(Repositories));
        RaisePropertyChanged(nameof(HasRepositories));
        RaisePropertyChanged(nameof(HasNoRepositories));
        RaisePropertyChanged(nameof(RepositoriesSummary));

        // Repositório novo ou tirado: o que está na tela não vale, e a releitura é já.
        var tracked = _settings.ActionsRepositories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_loadedRepositories is not null && _loadedRepositories.SetEquals(tracked))
        {
            Rebuild(_clock());
            return;
        }

        _load?.Cancel();
        _loadedRepositories = null;
        _historyAttempt = DateTimeOffset.MinValue;
        _durations = TrackedDurations();
        _scheduler.ExpireActions();
        Apply(null);
        _ = CheckAsync();
    }

    /// <summary>Os repositórios da leitura que está na tela.</summary>
    private HashSet<string>? _loadedRepositories;

    // ── O que a view mostra ─────────────────────────────────────────────────

    public ObservableCollection<RunnerGroupItem> RunnerGroups { get; } = new();

    public ObservableCollection<LaneItem> Lanes { get; } = new();

    public ObservableCollection<WaitingRunItem> WaitingRuns { get; } = new();

    public bool HasWaitingRuns => WaitingRuns.Count > 0;

    public ObservableCollection<HeldRunItem> HeldRuns { get; } = new();

    public bool HasHeldRuns => HeldRuns.Count > 0;

    public bool HasSnapshot => _snapshot is not null;

    public bool HasNoRunners => _snapshot is not null && RunnerGroups.Count == 0 && RunnerProblem is null;

    public bool HasNoQueue => _snapshot is not null && Lanes.Count == 0 && QueueProblem is null;

    public string? RunnerProblem => _snapshot is { RunnerProblems: [var first, ..] } ? first : _loadProblem;

    public bool HasRunnerProblem => RunnerProblem is not null;

    public string? QueueProblem => _snapshot is { QueueProblems.Count: > 0 } snapshot
        ? string.Join(" · ", snapshot.QueueProblems)
        : _loadProblem;

    public bool HasQueueProblem => QueueProblem is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            RaisePropertyChanged(nameof(StatusLine));
        }
    }

    /// <summary>Quando foi a última leitura e se o monitor está pausado ou recuando pela cota.</summary>
    public string StatusLine
    {
        get
        {
            if (IsLoading && _snapshot is null) return "Lendo o GitHub…";
            if (_scheduler.Profile == MonitorProfile.Off) return "Monitoramento desligado nas configurações.";

            var parts = new List<string>();
            if (_snapshot is { } snapshot) parts.Add($"Atualizado às {snapshot.ReadAt.ToLocalTime():HH:mm:ss}");
            if (IsLoading) parts.Add("lendo…");

            var now = _clock();
            if (_scheduler.Budget is { } budget && _scheduler.IsBackingOff(now))
                parts.Add($"em recuo · {budget.UsedFraction:P0} da cota REST usada · normaliza às {budget.ResetAt.ToLocalTime():HH:mm}");

            return string.Join(" · ", parts);
        }
    }

    // ── Laço ────────────────────────────────────────────────────────────────

    /// <summary>Começa o laço. Ele roda o tempo todo, mas só busca com alguma view à vista.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _loop = new CancellationTokenSource();
        _ = LoopAsync(_loop.Token);
        _ = CheckAsync();
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
        _load?.Cancel();
    }

    /// <summary>O mesmo que <see cref="Stop"/>, e ainda solta o token do laço. O do carregamento é solto por quem o criou.</summary>
    public void Dispose()
    {
        var loop = _loop;
        Stop();
        loop?.Dispose();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
                await CheckAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Laço parado.
        }
    }

    /// <summary>
    /// Atualizar do painel: lê agora, seja qual for a cadência. Com uma leitura em curso, ela
    /// termina e outra começa logo depois — a em curso pode ter saído antes do que mudou.
    /// </summary>
    public Task RefreshNowAsync()
    {
        if (IsLoading)
        {
            _rereadPending = true;
            return Task.CompletedTask;
        }

        _scheduler.ExpireActions();
        return CheckAsync();
    }

    /// <summary>
    /// Um passo: se alguma view está à vista e o ciclo venceu, lê o GitHub; senão, só refaz os
    /// tempos de espera na tela, que andam sozinhos. Fechado ou recolhido não chama nada.
    /// </summary>
    public async Task CheckAsync()
    {
        var now = _clock();

        StartHistoryIfDue(now);

        if (!IsShown || !HasRepositories || IsLoading || !_scheduler.IsActionsDue(now))
        {
            if (!IsMenuOpen) Rebuild(now);
            return;
        }

        _scheduler.MarkActionsChecked(now);
        var repositories = _settings.ActionsRepositories.ToList();
        var unknownAccess = repositories.Where(repository => !_access.ContainsKey(repository)).ToList();

        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        IsLoading = true;

        try
        {
            var accessTask = unknownAccess.Count == 0
                ? Task.FromResult<IReadOnlyDictionary<string, WriteAccess>>(new Dictionary<string, WriteAccess>())
                : ActionsQueueService.LoadAccessAsync(unknownAccess, _get, load.Token);
            var snapshot = await ActionsQueueService.LoadAsync(repositories, _get, now, load.Token).ConfigureAwait(true);
            var access = await accessTask.ConfigureAwait(true);
            if (load.IsCancellationRequested) return;

            foreach (var (repository, value) in access) _access[repository] = value;

            _scheduler.RecordBudget(snapshot.Budget);
            _loadProblem = null;
            _loadedRepositories = repositories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Fechou ou trocou de repositórios no meio: o que vier depois vale.
        }
        catch (Exception exception)
        {
            _loadProblem = exception.Message;
            Apply(_snapshot);
        }
        finally
        {
            if (ReferenceEquals(_load, load)) _load = null;
            load.Dispose();
            IsLoading = false;
        }

        if (_rereadPending)
        {
            _rereadPending = false;
            _scheduler.ExpireActions();
            await CheckAsync().ConfigureAwait(true);
        }
    }

    // ── Histórico de durações ───────────────────────────────────────────────

    /// <summary>A coleta do histórico em andamento, para quem precisa esperar por ela (os testes).</summary>
    internal Task HistoryTask => _history;

    /// <summary>
    /// Coleta o histórico dos repositórios cujas durações passaram de uma hora (ou nunca vieram),
    /// em segundo plano: com alguma view à vista, monitoramento ligado e fora do recuo pela cota.
    /// O ciclo do painel só lê o que já está em memória — a coleta nunca anda na cadência dele.
    /// </summary>
    private void StartHistoryIfDue(DateTimeOffset now)
    {
        if (_durationStore is null || _isCollecting || !IsShown || !HasRepositories) return;
        if (_scheduler.Profile == MonitorProfile.Off || _scheduler.IsBackingOff(now)) return;
        if (now - _historyAttempt < HistoryRetry) return;

        var stale = _settings.ActionsRepositories
            .Where(repository => !_durationsByRepository.TryGetValue(repository, out var durations)
                                 || now - durations.CollectedAt >= ActionsDurationStore.MaxAge)
            .ToList();
        if (stale.Count == 0) return;

        _historyAttempt = now;
        _isCollecting = true;
        RaisePropertyChanged(nameof(EstimateLine));
        _history = CollectHistoryAsync(stale, now);
    }

    private async Task CollectHistoryAsync(IReadOnlyList<string> repositories, DateTimeOffset now)
    {
        try
        {
            foreach (var repository in repositories)
            {
                RepositoryDurations? durations;
                try
                {
                    durations = await ActionsQueueService.LoadDurationsAsync(repository, _get, now).ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    durations = null;
                }

                // Falhou: fica o que havia, e tenta de novo depois do HistoryRetry.
                if (durations is not null) _durationsByRepository[repository] = durations;
            }

            // Repositório que saiu da configuração sai do arquivo.
            var tracked = _settings.ActionsRepositories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var gone in _durationsByRepository.Keys.Where(repository => !tracked.Contains(repository)).ToList())
                _durationsByRepository.Remove(gone);

            _durationStore?.Save(_durationsByRepository);
            _durations = TrackedDurations();
        }
        finally
        {
            _isCollecting = false;
            Rebuild(_clock());
        }
    }

    /// <summary>As medianas só dos repositórios acompanhados agora.</summary>
    private ActionsDurations TrackedDurations()
        => new(_durationsByRepository
            .Where(item => _settings.ActionsRepositories.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));

    /// <summary>A legenda da estimativa: de quando são as durações, ou que ainda não vieram.</summary>
    public string EstimateLine => _durations.CollectedAt is { } collected
        ? $"Início estimado pela duração mediana dos últimos {ActionsQueue.HistoryRuns} runs de cada workflow, coletada às {collected.ToLocalTime():HH:mm}. É aproximação, como a ordem."
        : _isCollecting
            ? "Coletando a duração dos jobs nos runs anteriores…"
            : "Sem histórico de duração ainda: a fila fica sem estimativa de início.";

    private void Apply(ActionsSnapshot? snapshot)
    {
        _snapshot = snapshot;

        // Run que saiu da leitura parou: o pedido de cancelamento dele está cumprido.
        if (snapshot is not null)
        {
            var present = snapshot.Runs.Select(run => (run.Repository, run.Id)).ToHashSet(RunKeyComparer.Instance);
            _cancelRequested.RemoveWhere(key => !present.Contains(key));
        }

        Rebuild(_clock());
    }

    // ── Agir sobre um run (#133) ────────────────────────────────────────────

    /// <summary>
    /// Quanto esperar entre o pedido e a releitura: o GitHub responde 202 na hora, mas o run
    /// leva uns segundos para sair de <c>in_progress</c>.
    /// </summary>
    private static readonly TimeSpan ActionSettleDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Um menu de run está aberto: o passo que só refaz os tempos de espera fica para depois —
    /// refazer as listas tira da tela o item em que o menu está, e o menu fecha na mão de quem
    /// ia clicar. Leitura nova do GitHub entra mesmo assim.
    /// </summary>
    public bool IsMenuOpen { get; set; }

    /// <summary>A releitura que veio depois da última ação. Só os testes esperam por ela.</summary>
    internal Task Reread { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Cancela o run — inteiro: a API não cancela um job sozinho. <paramref name="force"/> usa o
    /// <c>force-cancel</c>, que pula as condições <c>if: always()</c>. Devolve o que mostrar ao
    /// usuário quando o GitHub recusa, ou null. Dê certo ou não, o painel relê logo em seguida:
    /// num 409 o run já terminou e deve sumir da lista; num 403 a permissão é lida de novo.
    /// </summary>
    public async Task<string?> CancelRunAsync(RunTarget run, bool force)
    {
        var path = $"repos/{run.Repository}/actions/runs/{run.Id}/{(force ? "force-cancel" : "cancel")}";

        string? problem;
        try
        {
            var response = await _post(path, CancellationToken.None).ConfigureAwait(true);
            problem = ActionsQueueService.DescribeCancel(response, run.Repository);
            if (response.Status is 401 or 403) _access.Remove(run.Repository);
        }
        catch (Exception exception)
        {
            problem = exception.Message;
        }

        if (problem is null)
        {
            _cancelRequested.Add((run.Repository, run.Id));
            Rebuild(_clock());
        }

        Reread = RereadAfterActionAsync();
        return problem;
    }

    private async Task RereadAfterActionAsync()
    {
        await _settle().ConfigureAwait(true);
        await RefreshNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// O texto da confirmação: nomeia o run, o workflow, o repositório e a branch ou o PR, e diz
    /// que o run vai inteiro. O forçado diz também o que ele pula.
    /// </summary>
    internal static (string Title, string Headline, string Body, string Confirm) CancelConfirmation(RunTarget run, bool force)
    {
        var what = string.Join("\n", new[] { run.Title, $"{run.Name} · {run.Repository}", run.Ref }.OfType<string>());

        if (!force)
            return (
                "Cancelar run",
                $"Cancelar o run {run.Name}?",
                what + "\n\nCancela o run inteiro: todos os jobs dele, os que rodam e os que esperam — o GitHub não cancela um job "
                + "sozinho. Os passos com if: always() ainda rodam, como no Cancel workflow do GitHub. O runner fica livre e o "
                + "próximo da fila entra.",
                "Cancelar o run");

        return (
            "Forçar cancelamento",
            $"Forçar o cancelamento do run {run.Name}?",
            what + "\n\nPara o run inteiro sem rodar os passos com if: always() nem a limpeza que o workflow faria ao ser "
            + "cancelado: cache, artefato, lock ou ambiente que ele deixaria em ordem pode ficar pela metade.\n\nÉ para o run "
            + "que não para com o cancelamento comum. Se ainda não tentou, tente primeiro o Cancelar o run.",
            "Forçar cancelamento");
    }

    /// <summary>O alvo de cada run da leitura, com o que bloqueia agir sobre ele.</summary>
    private Func<string, long, RunTarget?> Targets(ActionsSnapshot snapshot)
    {
        var runs = new Dictionary<(string Repository, long Id), ActionsRun>(RunKeyComparer.Instance);
        foreach (var run in snapshot.Runs) runs.TryAdd((run.Repository, run.Id), run);

        return (repository, id) =>
        {
            if (!runs.TryGetValue((repository, id), out var run)) return null;

            var denied = _access.TryGetValue(repository, out var access) ? access.Reason : null;
            var requested = _cancelRequested.Contains((repository, id));
            return new RunTarget(
                run.Repository, run.Id, run.Workflow, run.Title, run.Number, run.Branch, run.PullRequest, run.Url,
                denied ?? (requested ? "Cancelamento já pedido: o GitHub ainda está parando o run." : null),
                denied)
            {
                IsCancelRequested = requested,
            };
        };
    }

    /// <summary>(repositório, id) sem caixa no repositório: owner/repo vem da configuração, digitado à mão.</summary>
    private sealed class RunKeyComparer : IEqualityComparer<(string Repository, long Id)>
    {
        public static readonly RunKeyComparer Instance = new();

        public bool Equals((string Repository, long Id) x, (string Repository, long Id) y)
            => x.Id == y.Id && string.Equals(x.Repository, y.Repository, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Repository, long Id) key)
            => HashCode.Combine(key.Id, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Repository));
    }

    /// <summary>
    /// Leva as listas à última leitura, mexendo só no item que mudou — o que não mudou fica, e o
    /// tooltip aberto sobre ele não pisca (#153). Runner que sumiu entre dois ciclos só sai da lista.
    /// </summary>
    private void Rebuild(DateTimeOffset now)
    {
        IReadOnlyList<RunnerGroupItem> groups = Array.Empty<RunnerGroupItem>();
        IReadOnlyList<LaneItem> lanes = Array.Empty<LaneItem>();
        IReadOnlyList<HeldRunItem> heldRuns = Array.Empty<HeldRunItem>();
        IReadOnlyList<WaitingRunItem> waitingRuns = Array.Empty<WaitingRunItem>();

        if (_snapshot is { } snapshot)
        {
            var targets = Targets(snapshot);
            var held = HeldKeys.Of(snapshot);
            groups = BuildRunnerGroups(snapshot, now, targets);
            lanes = BuildLanes(snapshot, now, targets, _durations);
            heldRuns = BuildHeld(snapshot, now, targets);
            waitingRuns = snapshot.WaitingRuns
                .Where(run => !held.Runs.Contains((run.Repository, run.Id)))
                .OrderBy(run => run.CreatedAt)
                .Select(run => new WaitingRunItem(
                    run.Workflow, Origin(run.Repository, run.Branch, run.PullRequest), Waiting(now - run.CreatedAt), targets(run.Repository, run.Id)))
                .ToList();
        }

        CollectionSync.ApplyGroups(
            RunnerGroups, groups,
            group => group.Runners,
            (group, runners) => group with { Runners = runners },
            (shown, fresh) => shown.Name == fresh.Name && shown.Summary == fresh.Summary);
        CollectionSync.ApplyGroups(
            Lanes, lanes,
            lane => lane.Jobs,
            (lane, jobs) => lane with { Jobs = jobs },
            (shown, fresh) => shown.Labels == fresh.Labels && shown.Summary == fresh.Summary);
        CollectionSync.Apply(HeldRuns, heldRuns);
        CollectionSync.Apply(WaitingRuns, waitingRuns);

        RaisePropertyChanged(nameof(HasSnapshot));
        RaisePropertyChanged(nameof(HasNoRunners));
        RaisePropertyChanged(nameof(HasNoQueue));
        RaisePropertyChanged(nameof(HasWaitingRuns));
        RaisePropertyChanged(nameof(HasHeldRuns));
        RaisePropertyChanged(nameof(RunnerProblem));
        RaisePropertyChanged(nameof(HasRunnerProblem));
        RaisePropertyChanged(nameof(QueueProblem));
        RaisePropertyChanged(nameof(HasQueueProblem));
        RaisePropertyChanged(nameof(StatusLine));
        RaisePropertyChanged(nameof(EstimateLine));
    }

    internal static IReadOnlyList<RunnerGroupItem> BuildRunnerGroups(
        ActionsSnapshot snapshot,
        DateTimeOffset now,
        Func<string, long, RunTarget?>? targets = null)
    {
        var jobs = ActionsQueue.JobsByRunner(snapshot.Runners, snapshot.Jobs);

        return snapshot.Runners
            .GroupBy(runner => runner.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var runners = group
                    .Select(runner =>
                    {
                        var state = !runner.IsOnline ? RunnerState.Offline : runner.IsBusy ? RunnerState.Busy : RunnerState.Idle;
                        var detail = string.Join(" · ", new[] { runner.Os, string.Join(", ", runner.Labels) }.Where(part => part.Length > 0));
                        jobs.TryGetValue(runner.Id, out var job);
                        return new RunnerItem(
                            runner.Name,
                            detail,
                            state,
                            job is null ? null : $"{job.Workflow} › {job.Name}",
                            job is null ? null : $"{Origin(job.Repository, job.Branch, job.PullRequest)} · {Running(now - (job.StartedAt ?? job.CreatedAt))}",
                            job is null ? null : targets?.Invoke(job.Repository, job.RunId));
                    })
                    .OrderBy(runner => runner.State)
                    .ThenBy(runner => runner.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var online = runners.Count(runner => runner.State != RunnerState.Offline);
                var busy = runners.Count(runner => runner.State == RunnerState.Busy);
                var summary = $"{busy} ocupado{(busy == 1 ? "" : "s")} de {online} online"
                              + (runners.Count > online ? $" · {runners.Count - online} offline" : "");
                return new RunnerGroupItem(group.Key, summary, runners);
            })
            .ToList();
    }

    internal static IReadOnlyList<LaneItem> BuildLanes(
        ActionsSnapshot snapshot,
        DateTimeOffset now,
        Func<string, long, RunTarget?>? targets = null,
        ActionsDurations? durations = null)
    {
        // Job segurado por concurrency não disputa runner: sai da fila e da conta da estimativa.
        var held = HeldKeys.Of(snapshot);
        var jobs = snapshot.Jobs.Where(job => !held.Jobs.Contains((job.Repository, job.Id))).ToList();
        var starts = ActionsQueue.EstimateStarts(snapshot.Runners, jobs, (durations ?? ActionsDurations.Empty).Median, now);

        return ActionsQueue.BuildLanes(jobs)
            .Select(lane => new LaneItem(
                lane.Labels.Count == 0 ? "sem labels" : string.Join(", ", lane.Labels),
                lane.Jobs.Count == 1 ? "1 esperando" : $"{lane.Jobs.Count} esperando",
                lane.Jobs.Select((job, index) => new QueuedJobItem(
                        $"{index + 1}º",
                        $"{job.Workflow} › {job.Name}",
                        Origin(job.Repository, job.Branch, job.PullRequest),
                        Waiting(now - job.CreatedAt),
                        Estimate(starts.GetValueOrDefault(job.Id)),
                        targets?.Invoke(job.Repository, job.RunId)))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Os segurados por concurrency que esta leitura conhece: o run na fila sem job, quando a
    /// concorrência é do workflow, e o job que não roda, quando é de job. Membro que não casa com
    /// nada da leitura (run que já começou, de outro estado) fica de fora: não há o que mostrar.
    /// </summary>
    internal static IReadOnlyList<HeldRunItem> BuildHeld(
        ActionsSnapshot snapshot,
        DateTimeOffset now,
        Func<string, long, RunTarget?>? targets = null)
    {
        var waiting = new Dictionary<(string Repository, long Id), ActionsWaitingRun>(RunKeyComparer.Instance);
        foreach (var run in snapshot.WaitingRuns) waiting.TryAdd((run.Repository, run.Id), run);
        var jobs = new Dictionary<(string Repository, long Id), ActionsJob>(RunKeyComparer.Instance);
        foreach (var job in snapshot.Jobs.Where(job => !job.IsRunning)) jobs.TryAdd((job.Repository, job.Id), job);

        var items = new List<(DateTimeOffset CreatedAt, HeldRunItem Item)>();
        var seen = new HashSet<(string Repository, long Id, bool IsJob)>();

        foreach (var hold in snapshot.Holds)
        {
            var holderTarget = targets?.Invoke(hold.Repository, hold.Holder.RunId);
            var holder = holderTarget?.Name ?? hold.Holder.RunName ?? $"o run {hold.Holder.RunId}";
            if (hold.Holder.JobName is { Length: > 0 } holderJob) holder = $"{holder} › {holderJob}";
            var holderUrl = hold.Holder.RunUrl ?? holderTarget?.Url;

            if (hold.JobId is { } jobId)
            {
                if (!jobs.TryGetValue((hold.Repository, jobId), out var job) || !seen.Add((job.Repository, job.Id, true))) continue;
                items.Add((job.CreatedAt, new HeldRunItem(
                    $"{job.Workflow} › {job.Name}", Origin(job.Repository, job.Branch, job.PullRequest), Waiting(now - job.CreatedAt),
                    hold.Group, holder, holderUrl, targets?.Invoke(job.Repository, job.RunId))));
            }
            else
            {
                if (!waiting.TryGetValue((hold.Repository, hold.RunId), out var run) || !seen.Add((run.Repository, run.Id, false))) continue;
                items.Add((run.CreatedAt, new HeldRunItem(
                    run.Workflow, Origin(run.Repository, run.Branch, run.PullRequest), Waiting(now - run.CreatedAt),
                    hold.Group, holder, holderUrl, targets?.Invoke(run.Repository, run.Id))));
            }
        }

        return items.OrderBy(item => item.CreatedAt).Select(item => item.Item).ToList();
    }

    /// <summary>Quem a concurrency segura, por (repositório, id): os runs inteiros e os jobs.</summary>
    private sealed record HeldKeys(HashSet<(string Repository, long Id)> Runs, HashSet<(string Repository, long Id)> Jobs)
    {
        public static HeldKeys Of(ActionsSnapshot snapshot)
        {
            var keys = new HeldKeys(new(RunKeyComparer.Instance), new(RunKeyComparer.Instance));
            foreach (var hold in snapshot.Holds)
                if (hold.JobId is { } job) keys.Jobs.Add((hold.Repository, job));
                else keys.Runs.Add((hold.Repository, hold.RunId));
            return keys;
        }
    }

    /// <summary>
    /// Quando deve começar, sempre com til: é estimativa. Zero é o runner que já devia ter
    /// liberado (ou está livre) — "a qualquer momento", nunca um tempo negativo.
    /// </summary>
    internal static string Estimate(TimeSpan? start)
    {
        if (start is not { } value) return NoEstimate;
        if (value <= TimeSpan.Zero) return "deve começar a qualquer momento";

        var minutes = TimeSpan.FromMinutes(Math.Ceiling(value.TotalMinutes));
        return $"deve começar em ~{Duration(minutes)}";
    }

    /// <summary>repo · PR #12 (branch), ou repo · branch sem PR. O dono sai: é o mesmo de todos, quase sempre.</summary>
    internal static string Origin(string repository, string? branch, int? pullRequest)
    {
        var name = repository.Split('/') is [_, var repo] ? repo : repository;
        if (pullRequest is { } number) return branch is null ? $"{name} · PR #{number}" : $"{name} · PR #{number} ({branch})";
        return branch is null ? name : $"{name} · {branch}";
    }

    internal static string Waiting(TimeSpan elapsed) => $"espera há {Duration(elapsed)}";

    internal static string Running(TimeSpan elapsed) => $"rodando há {Duration(elapsed)}";

    internal static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed < TimeSpan.FromMinutes(1)) return $"{(int)elapsed.TotalSeconds} s";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min";
        return elapsed.Minutes == 0 ? $"{(int)elapsed.TotalHours} h" : $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min";
    }
}
