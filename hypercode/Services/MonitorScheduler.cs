namespace Hypercode.Services;

/// <summary>Quão atento o monitoramento fica: multiplica todas as cadências.</summary>
public enum MonitorProfile { Off, Economical, Balanced, Aggressive }

/// <summary>Faixa de cadência de um worktree, do mais quente ao mais frio.</summary>
public enum CadenceTier
{
    /// <summary>PR aberto com Action rodando ou na fila.</summary>
    ChecksRunning,

    /// <summary>PR aberto, CI parada, sem aprovação ainda.</summary>
    AwaitingReview,

    /// <summary>PR aberto aprovado ou em draft: pouca coisa muda sozinha.</summary>
    OpenIdle,

    /// <summary>Branch pushada sem PR — o PR pode nascer pela web a qualquer momento.</summary>
    PushedWithoutPullRequest,

    /// <summary>PR mergeado ou fechado.</summary>
    Finished,

    /// <summary>Branch nunca pushada, ou o principal: não tem como ganhar PR.</summary>
    Dormant,
}

/// <summary>O que a consulta GraphQL devolve em rateLimit — a cota da conta, não só do app.</summary>
public sealed record GraphQLBudget(int Limit, int Used, int Cost, DateTimeOffset ResetAt)
{
    public double UsedFraction => Limit <= 0 ? 0 : (double)Used / Limit;
}

/// <summary>
/// Decide quais branches consultar no GitHub e quando. Cada branch tem a última conferência
/// e, opcionalmente, uma promoção temporária; a cadência sai do estado do PR, do perfil, da
/// janela e da cota. Não faz I/O: quem consulta é o <c>MainViewModel</c>, em lote.
/// </summary>
public sealed class MonitorScheduler
{
    /// <summary>Promoção depois de a branch ganhar upstream: é quando um PR está para nascer.</summary>
    public static readonly TimeSpan UpstreamPromotion = TimeSpan.FromMinutes(10);

    /// <summary>Promoção depois de um push numa branch que já tinha upstream e segue sem PR.</summary>
    public static readonly TimeSpan PushPromotion = TimeSpan.FromMinutes(3);

    /// <summary>Depois de rodar de novo os checks falhos: o run pode demorar a aparecer como pendente.</summary>
    public static readonly TimeSpan RerunPromotion = TimeSpan.FromMinutes(3);

    /// <summary>Cadência de quem está promovido.</summary>
    public static readonly TimeSpan PromotedCadence = TimeSpan.FromSeconds(30);

    /// <summary>Cadência do `git fetch`, que não gasta cota mas custa rede e disco.</summary>
    public static readonly TimeSpan FetchCadence = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Sondagem dos terminais: só o <c>ps -A -o tty=</c>, para apagar o ícone de quem fechou.
    /// Leitura da tabela de processos, sem descritores nem AppleScript.
    /// </summary>
    public static readonly TimeSpan TerminalProbeCadence = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Varredura completa dos terminais — <c>lsof -d cwd</c> e <c>ps</c> —, a única que enxerga um
    /// terminal novo aberto por fora do app. ~0,23 s por chamada, todos os worktrees de uma vez (#94).
    /// </summary>
    public static readonly TimeSpan TerminalScanCadence = TimeSpan.FromSeconds(15);

    /// <summary>Janela em segundo plano: tudo fica mais espaçado.</summary>
    public const double BackgroundFactor = 3;

    /// <summary>
    /// Quem vence em breve pega carona no lote de quem já venceu: a consulta custa por dezena
    /// de branches, não por branch, então adiantar um pouco sai de graça.
    /// </summary>
    private const double RideAlongFraction = 0.25;

    /// <summary>Até quantas branches a carona completa o lote — o GraphQL cobra 1 ponto a cada ~10.</summary>
    private const int RideAlongCeiling = 10;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTerminalProbe = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTerminalScan = DateTimeOffset.MinValue;

    public MonitorProfile Profile { get; set; } = MonitorProfile.Balanced;

    public bool IsWindowActive { get; set; } = true;

    public bool IsWindowMinimized { get; set; }

    /// <summary>Última cota lida; null antes da primeira consulta GraphQL.</summary>
    public GraphQLBudget? Budget { get; private set; }

    public bool IsPaused => Profile == MonitorProfile.Off || IsWindowMinimized;

    public static TimeSpan BaseCadence(CadenceTier tier) => tier switch
    {
        CadenceTier.ChecksRunning => TimeSpan.FromSeconds(30),
        CadenceTier.AwaitingReview => TimeSpan.FromMinutes(3),
        CadenceTier.OpenIdle => TimeSpan.FromMinutes(10),
        CadenceTier.PushedWithoutPullRequest => TimeSpan.FromMinutes(5),
        CadenceTier.Finished => TimeSpan.FromMinutes(20),
        _ => TimeSpan.FromMinutes(30),
    };

    /// <summary>Faixa de um worktree pelo PR que ele tem (ou não) e pelo estado local.</summary>
    public static CadenceTier Classify(PullRequestInfo? pullRequest, WorktreeStatus status, bool isMain)
    {
        if (pullRequest is not null)
        {
            if (!pullRequest.IsOpen) return CadenceTier.Finished;
            if (pullRequest.Checks == ChecksState.Pending) return CadenceTier.ChecksRunning;
            if (pullRequest.IsDraft || pullRequest.Review == ReviewState.Approved) return CadenceTier.OpenIdle;
            return CadenceTier.AwaitingReview;
        }

        if (isMain) return CadenceTier.Dormant;

        // Estado ainda não lido conta como pushada: é melhor conferir à toa que ficar cego.
        return status.IsKnown && !status.HasUpstream ? CadenceTier.Dormant : CadenceTier.PushedWithoutPullRequest;
    }

    /// <summary>
    /// Multiplicador de perfil, janela e cota juntos. Null com o monitoramento pausado.
    /// </summary>
    public double? Factor(DateTimeOffset now)
    {
        if (IsPaused) return null;

        var factor = Profile switch
        {
            MonitorProfile.Economical => 2.0,
            MonitorProfile.Aggressive => 0.5,
            _ => 1.0,
        };

        if (!IsWindowActive) factor *= BackgroundFactor;
        return factor * BudgetFactor(now);
    }

    /// <summary>
    /// Recuo pela cota do GraphQL, que é da conta inteira — outras ferramentas gastam dela
    /// também. Abaixo de 60 % não recua; passada a hora do reset, volta ao normal até a
    /// próxima consulta trazer o número novo.
    /// </summary>
    public double BudgetFactor(DateTimeOffset now)
    {
        if (Budget is not { } budget || now >= budget.ResetAt) return 1;

        return budget.UsedFraction switch
        {
            >= 0.9 => 10,
            >= 0.8 => 4,
            >= 0.6 => 2,
            _ => 1,
        };
    }

    public bool IsBackingOff(DateTimeOffset now) => BudgetFactor(now) > 1;

    public void RecordBudget(GraphQLBudget? budget)
    {
        if (budget is not null) Budget = budget;
    }

    /// <summary>Cadência efetiva de uma branch agora, com promoção, perfil, janela e cota.</summary>
    public TimeSpan? Cadence(string branch, CadenceTier tier, bool hasPullRequest, DateTimeOffset now)
    {
        if (Factor(now) is not { } factor) return null;

        var cadence = BaseCadence(tier);

        // A promoção existe para pegar o PR nascendo; quem já tem PR segue a faixa dele.
        if (!hasPullRequest
            && _entries.TryGetValue(branch, out var entry)
            && entry.PromotedUntil > now
            && PromotedCadence < cadence)
            cadence = PromotedCadence;

        return cadence * factor;
    }

    /// <summary>
    /// Branches que entram na próxima consulta: as vencidas e, se houver alguma, as que vencem
    /// logo, até completar um lote que não custa mais caro. Vazio se nada venceu.
    /// </summary>
    public IReadOnlyList<string> DueBranches(IEnumerable<ScheduledBranch> branches, DateTimeOffset now)
    {
        var due = new List<string>();
        var soon = new List<(string Branch, TimeSpan Remaining)>();

        foreach (var item in branches)
        {
            if (Cadence(item.Branch, item.Tier, item.HasPullRequest, now) is not { } cadence) return Array.Empty<string>();

            var last = _entries.TryGetValue(item.Branch, out var entry) ? entry.LastChecked : DateTimeOffset.MinValue;
            var remaining = last == DateTimeOffset.MinValue ? TimeSpan.Zero : last + cadence - now;

            if (remaining <= TimeSpan.Zero) due.Add(item.Branch);
            else if (remaining <= cadence * RideAlongFraction) soon.Add((item.Branch, remaining));
        }

        if (due.Count == 0) return due;

        foreach (var (branch, _) in soon.OrderBy(item => item.Remaining))
        {
            if (due.Count >= RideAlongCeiling) break;
            due.Add(branch);
        }

        return due;
    }

    public void MarkChecked(IEnumerable<string> branches, DateTimeOffset now)
    {
        foreach (var branch in branches) EntryOf(branch).LastChecked = now;
    }

    /// <summary>Encurta a cadência da branch por um tempo. Promoção mais longa não é encurtada.</summary>
    public void Promote(string branch, TimeSpan duration, DateTimeOffset now)
    {
        var entry = EntryOf(branch);
        var until = now + duration;
        if (until > entry.PromotedUntil) entry.PromotedUntil = until;
    }

    public bool IsPromoted(string branch, DateTimeOffset now)
        => _entries.TryGetValue(branch, out var entry) && entry.PromotedUntil > now;

    /// <summary>Esquece as branches que não estão mais em worktree nenhum.</summary>
    public void Retain(IReadOnlyCollection<string> branches)
    {
        var keep = branches.ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _entries.Keys.Where(branch => !keep.Contains(branch)).ToList()) _entries.Remove(stale);
    }

    /// <summary>O fetch segue perfil e janela, mas não a cota: ele não passa pela API.</summary>
    public bool IsFetchDue(DateTimeOffset now)
        => Factor(now) is { } factor && now - _lastFetch >= FetchCadence * (factor / BudgetFactor(now));

    public void MarkFetched(DateTimeOffset now) => _lastFetch = now;

    /// <summary>
    /// A leitura de terminal que venceu. Segue a janela — minimizada, nada; em segundo plano,
    /// mais espaçado —, mas não o perfil nem a cota: é leitura local, e um ícone que mente
    /// engana o duplo-clique.
    /// </summary>
    public TerminalCheck TerminalCheckDue(DateTimeOffset now)
    {
        if (IsWindowMinimized) return TerminalCheck.None;

        var factor = IsWindowActive ? 1 : BackgroundFactor;
        if (now - _lastTerminalScan >= TerminalScanCadence * factor) return TerminalCheck.Scan;
        if (now - _lastTerminalProbe >= TerminalProbeCadence * factor) return TerminalCheck.Probe;
        return TerminalCheck.None;
    }

    /// <summary>A varredura completa vale também como sondagem: ela relê os tty junto.</summary>
    public void MarkTerminalsScanned(DateTimeOffset now)
    {
        _lastTerminalScan = now;
        _lastTerminalProbe = now;
    }

    public void MarkTerminalsProbed(DateTimeOffset now) => _lastTerminalProbe = now;

    /// <summary>Repositório trocado: nada do histórico anterior vale.</summary>
    public void Reset()
    {
        _entries.Clear();
        _lastFetch = DateTimeOffset.MinValue;
    }

    private Entry EntryOf(string branch)
    {
        if (!_entries.TryGetValue(branch, out var entry))
        {
            entry = new Entry();
            _entries[branch] = entry;
        }

        return entry;
    }

    private sealed class Entry
    {
        public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset PromotedUntil { get; set; } = DateTimeOffset.MinValue;
    }
}

/// <summary>Que leitura de terminal venceu: nenhuma, só a sondagem dos <c>tty</c> ou a varredura completa.</summary>
public enum TerminalCheck { None, Probe, Scan }

/// <summary>Uma branch de worktree como o agendador a enxerga.</summary>
public readonly record struct ScheduledBranch(string Branch, CadenceTier Tier, bool HasPullRequest);
