using System.Collections.ObjectModel;
using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>Estado de um runner, do mais ao menos interessante — é a ordem da lista.</summary>
public enum RunnerState { Busy, Idle, Offline }

public sealed record RunnerItem(string Name, string Detail, RunnerState State, string? Job, string? JobOrigin)
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
}

public sealed record RunnerGroupItem(string Name, string Summary, IReadOnlyList<RunnerItem> Runners);

public sealed record QueuedJobItem(string Position, string Title, string Origin, string Wait);

public sealed record LaneItem(string Labels, string Summary, IReadOnlyList<QueuedJobItem> Jobs);

public sealed record WaitingRunItem(string Title, string Origin, string Wait);

/// <summary>
/// A fila do GitHub Actions (#130): os runners e o que espera por eles, nos repositórios
/// escolhidos nas configurações. Uma instância só, hospedada no painel lateral da janela e na
/// janela própria — as duas views mostram o mesmo estado. Com as duas fechadas, não busca nada.
/// A cadência é a do <see cref="MonitorScheduler"/>: perfil global, janela e recuo pela cota REST.
/// </summary>
public sealed class ActionsQueueViewModel : ObservableObject
{
    /// <summary>Quanto o laço espera entre uma pergunta e outra ao agendador — só conta em memória.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    public const string EstimateNotice =
        "Ordem estimada pelo horário de entrada na fila. O GitHub não expõe a posição: o despacho para self-hosted é FIFO na prática, mas não é contrato — e jobs de repositórios não acompanhados disputam os mesmos runners sem aparecer aqui.";

    private readonly Settings _settings;
    private readonly Action _save;
    private readonly Func<string, CancellationToken, Task<ApiResponse>> _get;
    private readonly Func<DateTimeOffset> _clock;
    private readonly MonitorScheduler _scheduler = new();

    private CancellationTokenSource? _loop;
    private CancellationTokenSource? _load;
    private ActionsSnapshot? _snapshot;
    private bool _isLoading;
    private string? _loadProblem;

    private bool _mainActive = true;
    private bool _mainMinimized;
    private bool _windowActive;
    private bool _windowMinimized;

    public ActionsQueueViewModel(
        Settings settings,
        Action save,
        Func<string, CancellationToken, Task<ApiResponse>>? get = null,
        Func<DateTimeOffset>? clock = null)
    {
        _settings = settings;
        _save = save;
        _get = get ?? ActionsQueueService.GetAsync;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
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

    public bool HasSnapshot => _snapshot is not null;

    public bool HasNoRunners => _snapshot is not null && RunnerGroups.Count == 0 && RunnerProblem is null;

    public bool HasNoQueue => _snapshot is not null && Lanes.Count == 0 && QueueProblem is null;

    public string? RunnerProblem => _snapshot?.RunnerProblems.FirstOrDefault() ?? _loadProblem;

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

    /// <summary>Atualizar do painel: lê agora, seja qual for a cadência.</summary>
    public Task RefreshNowAsync()
    {
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

        if (!IsShown || !HasRepositories || IsLoading || !_scheduler.IsActionsDue(now))
        {
            Rebuild(now);
            return;
        }

        _scheduler.MarkActionsChecked(now);
        var repositories = _settings.ActionsRepositories.ToList();

        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        IsLoading = true;

        try
        {
            var snapshot = await ActionsQueueService.LoadAsync(repositories, _get, now, load.Token).ConfigureAwait(true);
            if (load.IsCancellationRequested) return;

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
    }

    private void Apply(ActionsSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Rebuild(_clock());
    }

    /// <summary>Refaz as listas a partir da última leitura. Runner que sumiu entre dois ciclos só sai da lista.</summary>
    private void Rebuild(DateTimeOffset now)
    {
        RunnerGroups.Clear();
        Lanes.Clear();
        WaitingRuns.Clear();

        if (_snapshot is { } snapshot)
        {
            foreach (var group in BuildRunnerGroups(snapshot, now)) RunnerGroups.Add(group);
            foreach (var lane in BuildLanes(snapshot, now)) Lanes.Add(lane);
            foreach (var run in snapshot.WaitingRuns.OrderBy(run => run.CreatedAt))
                WaitingRuns.Add(new WaitingRunItem(run.Workflow, Origin(run.Repository, run.Branch, run.PullRequest), Waiting(now - run.CreatedAt)));
        }

        RaisePropertyChanged(nameof(HasSnapshot));
        RaisePropertyChanged(nameof(HasNoRunners));
        RaisePropertyChanged(nameof(HasNoQueue));
        RaisePropertyChanged(nameof(HasWaitingRuns));
        RaisePropertyChanged(nameof(RunnerProblem));
        RaisePropertyChanged(nameof(HasRunnerProblem));
        RaisePropertyChanged(nameof(QueueProblem));
        RaisePropertyChanged(nameof(HasQueueProblem));
        RaisePropertyChanged(nameof(StatusLine));
    }

    internal static IReadOnlyList<RunnerGroupItem> BuildRunnerGroups(ActionsSnapshot snapshot, DateTimeOffset now)
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
                            job is null ? null : $"{Origin(job.Repository, job.Branch, job.PullRequest)} · {Running(now - (job.StartedAt ?? job.CreatedAt))}");
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

    internal static IReadOnlyList<LaneItem> BuildLanes(ActionsSnapshot snapshot, DateTimeOffset now)
        => ActionsQueue.BuildLanes(snapshot.Jobs)
            .Select(lane => new LaneItem(
                lane.Labels.Count == 0 ? "sem labels" : string.Join(", ", lane.Labels),
                lane.Jobs.Count == 1 ? "1 esperando" : $"{lane.Jobs.Count} esperando",
                lane.Jobs.Select((job, index) => new QueuedJobItem(
                        $"{index + 1}º",
                        $"{job.Workflow} › {job.Name}",
                        Origin(job.Repository, job.Branch, job.PullRequest),
                        Waiting(now - job.CreatedAt)))
                    .ToList()))
            .ToList();

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
