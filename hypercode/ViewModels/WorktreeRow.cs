using Hypercode.Services;

namespace Hypercode.ViewModels;

public sealed class WorktreeRow : ObservableObject
{
    private PullRequestInfo? _pullRequest;
    private WorktreeStatus _status = WorktreeStatus.Unknown;
    private BaseDistance? _baseDistance;
    private IReadOnlyList<string> _pullRequestChanges = Array.Empty<string>();
    private string? _cleanupNote;
    private bool _hasClaudeSession;
    private TerminalPresence _terminal = TerminalPresence.None;

    // O que a interface mostra fica guardado e só é trocado (e avisado) quando o conteúdo muda:
    // uma lista nova a cada leitura faz o ItemsControl recriar os badges, e o tooltip aberto
    // sobre um deles fecha e reabre — pisca no ritmo do monitoramento (#153).
    private IReadOnlyList<StatusBadge> _gitBadges = Array.Empty<StatusBadge>();
    private IReadOnlyList<StatusBadge> _pullRequestBadges = Array.Empty<StatusBadge>();
    private string _tags = string.Empty;
    private string? _tagsTooltip;
    private string? _pullRequestTooltip;

    public WorktreeRow(WorktreeInfo worktree)
    {
        Worktree = worktree;
        _hasClaudeSession = ClaudeSessions.Exist(worktree.FullPath);
        RefreshTags();
    }

    public WorktreeInfo Worktree { get; }

    public string Name => Worktree.Name;

    public string FullPath => Worktree.FullPath;

    public string Branch => Worktree.Branch
        ?? (Worktree.RebasingBranch is { } rebasing ? $"{rebasing} (rebase em andamento)" : null)
        ?? (Worktree.IsBare ? "(bare)" : $"detached @ {Worktree.ShortHead}");

    public PullRequestInfo? PullRequest
    {
        get => _pullRequest;
        set
        {
            if (SetProperty(ref _pullRequest, value))
            {
                RaisePropertyChanged(nameof(PullRequestLabel));
                SetProperty(ref _pullRequestTooltip, BuildPullRequestTooltip(), nameof(PullRequestTooltip));
                RaisePropertyChanged(nameof(HasPullRequest));
                RaisePropertyChanged(nameof(BaseBranch));
                RaisePropertyChanged(nameof(CanUpdateFromBase));
                RaisePropertyChanged(nameof(CanRerunFailedChecks));
                RefreshTags();
            }
        }
    }

    public bool HasPullRequest => _pullRequest is not null;

    /// <summary>
    /// Transições do PR avisadas pelo monitoramento e ainda não marcadas como vistas
    /// (checks, review, merge, conflito). Enquanto houver, a linha ganha o sino.
    /// </summary>
    public IReadOnlyList<string> PullRequestChanges
    {
        get => _pullRequestChanges;
        set
        {
            value ??= Array.Empty<string>();
            if (_pullRequestChanges.SequenceEqual(value)) return;
            _pullRequestChanges = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasPullRequestChanges));
            RefreshBadges();
        }
    }

    public bool HasPullRequestChanges => _pullRequestChanges.Count > 0;

    /// <summary>
    /// Com a limpeza automática ligada, desde quando ela vê este worktree concluído e quando
    /// ele sai (ou por que ficou). Vai para o tooltip do PR mergeado/fechado e do removível.
    /// </summary>
    public string? CleanupNote
    {
        get => _cleanupNote;
        set
        {
            if (_cleanupNote == value) return;
            _cleanupNote = value;
            RefreshBadges();
        }
    }

    /// <summary>Base do PR aberto desta branch — de onde vem o "atualizar a partir da base".</summary>
    public string? BaseBranch => _pullRequest is { IsOpen: true, BaseRefName: { Length: > 0 } baseRef } ? baseRef : null;

    /// <summary>
    /// Quanto a branch está atrás de &lt;remoto&gt;/&lt;base&gt;, medido no worktree com as refs
    /// locais — sem fetch, então pode estar defasado até o próximo fetch.
    /// </summary>
    public BaseDistance? BaseDistance
    {
        get => _baseDistance;
        set
        {
            if (!SetProperty(ref _baseDistance, value)) return;
            RaisePropertyChanged(nameof(CanUpdateFromBase));
            RefreshBadges();
        }
    }

    public string PullRequestLabel => _pullRequest is null ? string.Empty : $"#{_pullRequest.Number}";

    public string? PullRequestTooltip => _pullRequestTooltip;

    private string? BuildPullRequestTooltip() => _pullRequest is null
        ? null
        : $"{_pullRequest.Title}\n{_pullRequest.State}{(_pullRequest.IsDraft ? " (draft)" : string.Empty)}\n{_pullRequest.Url}";

    /// <summary>Etiquetas curtas mostradas abaixo do nome.</summary>
    public string Tags => _tags;

    /// <summary>Explicação de cada etiqueta presente nesta linha, para o tooltip.</summary>
    public string? TagsTooltip => _tagsTooltip;

    private string? BuildTagsTooltip()
    {
        var explanations = ActiveTags().Select(ExplainTag).ToList();
        return explanations.Count == 0 ? null : string.Join("\n\n", explanations);
    }

    /// <summary>
    /// Candidato à limpeza: PR mergeado ou fechado, worktree que o git já considera órfão, ou
    /// branch remota apagada com o HEAD já contido na base remota. Branch apagada fora da base
    /// não entra: pode ter sido apagada sem merge.
    /// O principal e o bare nunca entram (não são removíveis). Lock manual também protege,
    /// mas lock de ferramenta (supacode e afins) é só bookkeeping: entra, e a limpeza destrava antes.
    /// </summary>
    public bool IsCompleted =>
        !Worktree.IsMain
        && !Worktree.IsBare
        && (!Worktree.IsLocked || Worktree.IsToolLock)
        && Worktree.RebasingBranch is null
        && (Worktree.IsPrunable
            || (_pullRequest is not null && !_pullRequest.IsOpen)
            || IsGoneAndInBase);

    /// <summary>
    /// Sabe-se que este worktree não é candidato: PR aberto, rebase pausado, ou principal, bare
    /// ou travado à mão. Com o rebase, a linha casa com o PR da branch rebaseada, que pode já
    /// estar mergeado — e a limpeza não pode levar um worktree no meio da operação. É mais forte que <c>!IsCompleted</c>, que também vale para o PR que ainda não
    /// chegou — e nesse caso a limpeza automática não pode esquecer que ele já estava concluído.
    /// </summary>
    public bool IsKnownNotCompleted =>
        Worktree.IsMain
        || Worktree.IsBare
        || (Worktree.IsLocked && !Worktree.IsToolLock)
        || Worktree.RebasingBranch is not null
        || _pullRequest is { IsOpen: true };

    /// <summary>Upstream "gone" e HEAD contido na base — dispensa achar o PR. Não vale com PR aberto.</summary>
    private bool IsGoneAndInBase =>
        _status.ContainedInBase is not null && _pullRequest is not { IsOpen: true };

    /// <summary>Por que esta linha entrou na limpeza — usado no diálogo de confirmação.</summary>
    public string CompletionReason
    {
        get
        {
            if (Worktree.IsPrunable) return "órfão (a pasta não existe mais)";
            if (_pullRequest is null)
                return IsGoneAndInBase
                    ? $"branch remota apagada e já contida em {_status.ContainedInBase}"
                    : string.Empty;
            return _pullRequest.State.ToUpperInvariant() switch
            {
                "MERGED" => $"PR #{_pullRequest.Number} mergeado",
                "CLOSED" => $"PR #{_pullRequest.Number} fechado sem merge",
                _ => $"PR #{_pullRequest.Number} {_pullRequest.State.ToLowerInvariant()}",
            };
        }
    }

    public WorktreeStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            RaisePropertyChanged(nameof(CanUpdateBranch));
            RaisePropertyChanged(nameof(IsCompleted));
            RaisePropertyChanged(nameof(CompletionReason));
            RefreshBadges();
        }
    }

    /// <summary>Ícones do estado local do worktree (coluna ESTADO).</summary>
    public IReadOnlyList<StatusBadge> GitBadges => _gitBadges;

    private List<StatusBadge> BuildGitBadges()
    {
        var badges = new List<StatusBadge>();

        if (TerminalBadge() is { } terminal) badges.Add(terminal);

        if (_status.PendingOperation is { } operation)
            badges.Add(new StatusBadge(
                BadgeKind.OperationPending,
                $"{operation} em andamento neste worktree — conclua com git {operation} --continue ou desfaça com --abort."));

        if (_status.HasUnmergedPaths)
            badges.Add(new StatusBadge(BadgeKind.Conflicted, "Arquivos em conflito ainda não resolvidos."));
        else if (_status.HasUncommittedChanges)
            badges.Add(new StatusBadge(
                BadgeKind.Dirty,
                "Alterações não commitadas. A limpeza não remove worktree sujo — o git recusa sem --force."));

        if (_status.IsKnown && Worktree.Branch is not null)
        {
            if (!_status.HasUpstream)
                badges.Add(new StatusBadge(BadgeKind.NeverPushed, "A branch não tem upstream — nunca foi pushada."));
            else if (_status.IsUpstreamGone)
                badges.Add(new StatusBadge(
                    BadgeKind.UpstreamGone,
                    "A branch remota foi apagada (upstream \"gone\"), normalmente depois do merge do PR. "
                    + (_status.ContainedInBase is { } baseRef
                        ? $"O HEAD já está contido em {baseRef}, então o worktree entra em Limpar concluídos."
                        : "O HEAD não está contido na base remota (ou foi squash/rebase merge): só entra em "
                          + "Limpar concluídos se o PR mergeado for encontrado. Senão, remova pelo menu.")));
            else if (_status.IsDiverged)
                badges.Add(new StatusBadge(
                    BadgeKind.Diverged,
                    $"Divergiu: {_status.Ahead} à frente e {_status.Behind} atrás do upstream. Precisa merge ou rebase."));
            else if (_status.Ahead > 0)
                badges.Add(new StatusBadge(BadgeKind.Ahead, $"{_status.Ahead} commit(s) esperando push."));
            else if (_status.Behind > 0)
                badges.Add(new StatusBadge(BadgeKind.Behind, $"{_status.Behind} commit(s) esperando pull."));
        }

        if (IsCompleted)
            badges.Add(new StatusBadge(
                BadgeKind.Removable,
                $"Pode ser removido — {CompletionReason}."
                + (_status.HasUncommittedChanges ? " Mas há alterações não commitadas: o git vai recusar." : string.Empty)
                + WithCleanupNote()));

        return badges;
    }

    /// <summary>Ícones do PR (coluna PR), ao lado do número.</summary>
    public IReadOnlyList<StatusBadge> PullRequestBadges => _pullRequestBadges;

    private List<StatusBadge> BuildPullRequestBadges()
    {
        var badges = new List<StatusBadge>();

        // Sem PR a coluna fica vazia — nada de marcador de ausência.
        if (_pullRequest is not { } pullRequest) return badges;

        if (_pullRequestChanges.Count > 0)
            badges.Add(new StatusBadge(
                BadgeKind.PrChanged,
                $"Mudou desde a última olhada:\n• {string.Join("\n• ", _pullRequestChanges)}\n\nBotão direito → Marcar como visto."));

        badges.Add(pullRequest.State.ToUpperInvariant() switch
        {
            "MERGED" => new StatusBadge(BadgeKind.PrMerged, $"PR #{pullRequest.Number} mergeado.{WithCleanupNote()}"),
            "CLOSED" => new StatusBadge(BadgeKind.PrClosed, $"PR #{pullRequest.Number} fechado sem merge.{WithCleanupNote()}"),
            _ => pullRequest.IsDraft
                ? new StatusBadge(BadgeKind.PrDraft, $"PR #{pullRequest.Number} aberto como rascunho.")
                : new StatusBadge(BadgeKind.PrOpen, $"PR #{pullRequest.Number} aberto."),
        });

        // Checks, review e mergeabilidade só fazem sentido enquanto o PR está aberto.
        if (!pullRequest.IsOpen) return badges;

        if (pullRequest.HasConflicts)
            badges.Add(new StatusBadge(BadgeKind.NeedsRebase, "Conflito com a base — precisa rebase ou merge antes de integrar."));
        else if (pullRequest.IsBehindBase)
            badges.Add(new StatusBadge(BadgeKind.NeedsRebase, BehindBaseTooltip(pullRequest)));

        switch (pullRequest.Checks)
        {
            case ChecksState.Passing:
                badges.Add(new StatusBadge(BadgeKind.ChecksPassing, "Todos os checks passaram."));
                break;
            case ChecksState.Failing:
                var failing = pullRequest.FailingChecksSummary;
                badges.Add(new StatusBadge(
                    BadgeKind.ChecksFailing,
                    string.IsNullOrEmpty(failing) ? "Há checks falhando." : $"Checks falhando: {failing}"));
                break;
            case ChecksState.Pending:
                badges.Add(new StatusBadge(BadgeKind.ChecksPending, "Checks ainda rodando."));
                break;
        }

        switch (pullRequest.Review)
        {
            case ReviewState.Approved:
                badges.Add(new StatusBadge(BadgeKind.ReviewApproved, "Review aprovado."));
                break;
            case ReviewState.ChangesRequested:
                badges.Add(new StatusBadge(BadgeKind.ReviewChangesRequested, "Review pediu mudanças."));
                break;
            case ReviewState.Required:
                badges.Add(new StatusBadge(BadgeKind.ReviewRequired, "Aguardando review."));
                break;
        }

        return badges;
    }

    /// <summary>
    /// Nomeia a base e a distância, e deixa claro que pull não resolve: o ícone fala da base,
    /// o pull traz o upstream da própria branch — que normalmente já está em dia.
    /// </summary>
    private string BehindBaseTooltip(PullRequestInfo pullRequest)
    {
        const string hint = "Traga a base para dentro da branch (Atualizar a partir da base…) — git pull na própria branch não resolve isto.";

        if (string.IsNullOrEmpty(pullRequest.BaseRefName))
            return $"A base andou desde que a branch saiu. {hint}";

        var distance = _baseDistance is { } known && known.Branch == pullRequest.BaseRefName && known.Behind > 0
            ? $"está {known.Behind} commit{(known.Behind == 1 ? "" : "s")} à frente"
            : "andou desde que a branch saiu";

        return $"A base {pullRequest.BaseRefName} {distance}. {hint}";
    }

    /// <summary>
    /// Terminal aberto na pasta: o que é, e que o duplo-clique vai para ele. Só uma etiqueta
    /// por linha, nesta ordem: terminal, sessão de multiplexador, processo. A de multiplexador
    /// (o zmx do supacode, um tmux) sai apagada e com outro ícone: tem tty, mas não há janela
    /// para onde ir. Processo sem terminal (um dotnet watch) também acende o ícone, apagado: a
    /// pasta está em uso, mas não há sessão para onde ir.
    /// </summary>
    private StatusBadge? TerminalBadge()
    {
        if (_terminal.IsEmpty) return null;

        var processes = string.Join(", ", _terminal.Processes);
        var terminals = _terminal.Sessions.Where(session => session.App is not TerminalApp.Multiplexer).ToList();
        var multiplexers = _terminal.Sessions.Where(session => session.App is TerminalApp.Multiplexer).ToList();

        if (terminals.Count == 0 && multiplexers.Count > 0)
        {
            var multiplexerLines = new List<string>
            {
                multiplexers.Count == 1
                    ? $"Sessão do {multiplexers[0].AppName} aberta aqui {Age(multiplexers[0])}."
                    : $"{multiplexers.Count} sessões de multiplexador abertas aqui:\n• "
                      + string.Join("\n• ", multiplexers.Select(session => $"{session.AppName}, {Age(session)}")),
                "Não é uma janela de terminal: está desanexada, ou dentro de outro app, e daqui não dá para ir até ela. O duplo-clique abre um terminal novo.",
            };
            if (_terminal.Processes.Count > 0) multiplexerLines.Add($"Fora dela, com a pasta aberta: {processes}.");

            return new StatusBadge(BadgeKind.MultiplexerSession, string.Join("\n\n", multiplexerLines));
        }

        if (terminals.Count == 0)
            return new StatusBadge(
                BadgeKind.ProcessOpen,
                $"Há processo com esta pasta aberta, mas nenhuma sessão de terminal: {processes}. O duplo-clique abre um terminal novo.");

        static string Describe(TerminalSession session)
            => session.Name is { Length: > 0 } name ? $"{session.AppName} — {name}" : $"{session.AppName} ({session.Tty})";

        static string Age(TerminalSession session) => LockRecord.Relative(session.Age) ?? "agora há pouco";

        var lines = new List<string>
        {
            terminals.Count == 1
                ? $"Terminal aberto neste worktree: {Describe(terminals[0])}."
                : $"{terminals.Count} sessões de terminal abertas neste worktree, da mais recente para a mais antiga:\n• "
                  + string.Join("\n• ", terminals.Select(Describe)),
        };

        lines.Add(_terminal.Focusable is { } target
            ? (terminals.Count == 1 ? "Duplo-clique vai para ele" : $"Duplo-clique vai para a mais recente ({Describe(target)})")
              + "; para abrir outro, botão direito → Abrir o terminal."
            : "Não dá para trazê-lo para a frente daqui: só o iTerm2 e o Terminal sabem. O duplo-clique abre um novo.");
        if (multiplexers.Count > 0)
            lines.Add("Há também sessão de multiplexador, sem janela: " + string.Join(", ", multiplexers.Select(session => session.AppName)) + ".");
        if (_terminal.Processes.Count > 0) lines.Add($"Fora do terminal, com a pasta aberta: {processes}.");

        return new StatusBadge(BadgeKind.TerminalOpen, string.Join("\n\n", lines));
    }

    private string WithCleanupNote() => _cleanupNote is null ? string.Empty : $"\n\n{_cleanupNote}";

    /// <summary>
    /// Recalcula os badges e só avisa a interface da lista que mudou de conteúdo. <see cref="StatusBadge"/>
    /// é record: a comparação item a item já é por valor.
    /// </summary>
    private void RefreshBadges()
    {
        SetBadges(ref _gitBadges, BuildGitBadges(), nameof(GitBadges));
        SetBadges(ref _pullRequestBadges, BuildPullRequestBadges(), nameof(PullRequestBadges));
    }

    private void SetBadges(ref IReadOnlyList<StatusBadge> field, IReadOnlyList<StatusBadge> value, string propertyName)
    {
        if (field.SequenceEqual(value)) return;
        field = value;
        RaisePropertyChanged(propertyName);
    }

    /// <summary>
    /// Um termo do filtro casa com nome, branch, número do PR (com ou sem #) ou título do PR.
    /// Sem diferenciar maiúsculas nem acentos.
    /// </summary>
    public bool Matches(string term)
    {
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions options =
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;

        bool Contains(string? text) => !string.IsNullOrEmpty(text) && compare.IndexOf(text, term, options) >= 0;

        if (Contains(Name) || Contains(Branch)) return true;
        if (_pullRequest is not { } pullRequest) return false;

        var number = term.TrimStart('#');
        return (number.Length > 0 && pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture).StartsWith(number, StringComparison.Ordinal))
               || Contains(pullRequest.Title);
    }

    public bool CanLaunch => Directory.Exists(Worktree.FullPath) && !Worktree.IsBare;

    /// <summary>O principal e o bare não são removíveis com git worktree remove.</summary>
    public bool CanRemove => !Worktree.IsMain && !Worktree.IsBare;

    /// <summary>
    /// Só o vinculado aceita lock: o git recusa travar o principal. O órfão fica de fora
    /// porque a trava só impediria o prune de limpar seus metadados.
    /// </summary>
    public bool CanLock => CanRemove && !Worktree.IsLocked && !Worktree.IsPrunable;

    public bool CanUnlock => CanRemove && Worktree.IsLocked;

    /// <summary>Só dá para puxar numa branch que tem upstream — senão não há de onde puxar.</summary>
    public bool CanUpdateBranch => CanLaunch && Worktree.Branch is not null && _status is { IsKnown: true, HasUpstream: true };

    /// <summary>
    /// A base do PR tem commits que a branch não tem: o GitHub diz BEHIND ou em conflito,
    /// ou a contagem local achou commits. A confirmação final (e a contagem exata) vem do
    /// fetch feito na hora de atualizar.
    /// </summary>
    public bool CanUpdateFromBase =>
        CanLaunch
        && Worktree.Branch is not null
        && BaseBranch is not null
        && (_pullRequest!.IsBehindBase || _pullRequest.HasConflicts || _baseDistance is { Behind: > 0 });

    /// <summary>
    /// PR aberto com checks falhando e ao menos um deles de um workflow run do Actions — o que
    /// o `gh run rerun --failed` sabe rodar de novo. Falha só de StatusContext (CI externo) não conta.
    /// </summary>
    public bool CanRerunFailedChecks =>
        _pullRequest is { IsOpen: true, Checks: ChecksState.Failing } pullRequest
        && pullRequest.FailedWorkflowRuns.Count > 0;

    /// <summary>
    /// Há sessão do Claude Code gravada para esta pasta — condição para o `claude --continue`
    /// ter o que retomar. Lido quando a linha é criada e relido quando a janela volta para a frente
    /// ou o estado das linhas é relido: a sessão costuma nascer num terminal que o próprio app abriu.
    /// </summary>
    public bool HasClaudeSession
    {
        get => _hasClaudeSession;
        set => SetProperty(ref _hasClaudeSession, value);
    }

    /// <summary>
    /// Terminal e processos com a pasta de trabalho dentro deste worktree, pelo lsof. Relido com
    /// o estado das linhas e quando a janela volta para a frente; se a leitura falha, fica o de
    /// antes — não saber não é o mesmo que não haver.
    /// </summary>
    public TerminalPresence Terminal
    {
        get => _terminal;
        set
        {
            if (!SetProperty(ref _terminal, value ?? TerminalPresence.None)) return;
            RaisePropertyChanged(nameof(CanFocusTerminal));
            RefreshBadges();
        }
    }

    /// <summary>Há sessão de terminal aqui a que dá para dar foco — iTerm2 ou Terminal.</summary>
    public bool CanFocusTerminal => _terminal.Focusable is not null;

    /// <summary>Por que não dá para ir ao terminal deste worktree; null quando dá.</summary>
    public string? FocusTerminalUnavailableReason
    {
        get
        {
            if (CanFocusTerminal) return null;

            var apps = _terminal.Sessions.Select(session => session.AppName).Distinct(StringComparer.Ordinal).ToList();
            if (apps.Count == 0) return "Nenhum terminal aberto neste worktree.";

            var owners = string.Join(", ", apps);
            return _terminal.Sessions.All(session => session.App is TerminalApp.Multiplexer)
                ? $"Só há sessão do {owners}, sem janela para onde ir."
                : $"A sessão aberta aqui é do {owners}, que não expõe as janelas pelo tty: só o iTerm2 e o Terminal deixam trazê-las para a frente.";
        }
    }

    private void RefreshTags()
    {
        SetProperty(ref _tags, string.Join(" · ", ActiveTags()), nameof(Tags));
        SetProperty(ref _tagsTooltip, BuildTagsTooltip(), nameof(TagsTooltip));
        RaisePropertyChanged(nameof(IsCompleted));
        RaisePropertyChanged(nameof(CompletionReason));
        RefreshBadges();
    }

    private List<string> ActiveTags()
    {
        var tags = new List<string>();

        if (Worktree.IsMain) tags.Add("principal");
        if (Worktree.IsBare) tags.Add("bare");
        if (Worktree.IsLocked) tags.Add("travado");
        if (Worktree.IsPrunable) tags.Add("órfão");

        if (_pullRequest is { IsDraft: true } draft && draft.IsOpen) tags.Add("draft");
        else if (_pullRequest is { } pr && !pr.IsOpen) tags.Add(pr.State.ToLowerInvariant());

        return tags;
    }

    private string ExplainTag(string tag) => tag switch
    {
        "principal" =>
            "principal — é o worktree original do repositório, onde fica o diretório .git de verdade. "
            + "Os demais são vinculados, criados com git worktree add. Este não pode ser removido com git worktree remove.",

        "bare" =>
            "bare — repositório sem árvore de trabalho. Não há arquivos para abrir no terminal.",

        "travado" when Worktree.IsToolLock =>
            $"travado por {Worktree.LockOwner} — lock de bookkeeping da ferramenta, não um pedido seu. "
            + "A limpeza destrava antes de remover.",

        "travado" =>
            "travado — alguém rodou git worktree lock aqui"
            + (string.IsNullOrEmpty(Worktree.LockDescription) ? "" : $" (motivo: {Worktree.LockDescription})")
            + ". O git não o remove no prune e a limpeza do Hypercode também o ignora. "
            + "Destrave pelo menu, em Destravar o worktree….",

        "órfão" =>
            "órfão — o git marcou como prunable: o ponteiro aponta para uma pasta que não existe mais, "
            + "normalmente porque você apagou o diretório na mão. Um git worktree prune limpa os metadados.",

        "draft" =>
            "draft — o PR desta branch está aberto como rascunho.",

        "merged" =>
            "merged — o PR desta branch foi mergeado. Candidato à limpeza.",

        "closed" =>
            "closed — o PR desta branch foi fechado sem merge. Candidato à limpeza.",

        _ => tag,
    };
}

/// <summary>Distância entre a branch e &lt;remoto&gt;/&lt;base&gt;.</summary>
public sealed record BaseDistance(string Remote, string Branch, int Behind, int Ahead)
{
    public string Ref => $"{Remote}/{Branch}";
}
