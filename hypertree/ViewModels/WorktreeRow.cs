using Hypertree.Services;

namespace Hypertree.ViewModels;

public sealed class WorktreeRow : ObservableObject
{
    private PullRequestInfo? _pullRequest;
    private WorktreeStatus _status = WorktreeStatus.Unknown;

    public WorktreeRow(WorktreeInfo worktree)
    {
        Worktree = worktree;
        HasClaudeSession = ClaudeSessions.Exist(worktree.FullPath);
    }

    public WorktreeInfo Worktree { get; }

    public string Name => Worktree.Name;

    public string FullPath => Worktree.FullPath;

    public string Branch => Worktree.Branch
        ?? (Worktree.IsBare ? "(bare)" : $"detached @ {Worktree.ShortHead}");

    public PullRequestInfo? PullRequest
    {
        get => _pullRequest;
        set
        {
            if (SetProperty(ref _pullRequest, value))
            {
                RaisePropertyChanged(nameof(PullRequestLabel));
                RaisePropertyChanged(nameof(PullRequestTooltip));
                RaisePropertyChanged(nameof(HasPullRequest));
                RefreshTags();
            }
        }
    }

    public bool HasPullRequest => _pullRequest is not null;

    public string PullRequestLabel => _pullRequest is null ? string.Empty : $"#{_pullRequest.Number}";

    public string? PullRequestTooltip => _pullRequest is null
        ? null
        : $"{_pullRequest.Title}\n{_pullRequest.State}{(_pullRequest.IsDraft ? " (draft)" : string.Empty)}\n{_pullRequest.Url}";

    /// <summary>Etiquetas curtas mostradas abaixo do nome.</summary>
    public string Tags => string.Join(" · ", ActiveTags());

    /// <summary>Explicação de cada etiqueta presente nesta linha, para o tooltip.</summary>
    public string? TagsTooltip
    {
        get
        {
            var explanations = ActiveTags().Select(ExplainTag).ToList();
            return explanations.Count == 0 ? null : string.Join("\n\n", explanations);
        }
    }

    /// <summary>
    /// Candidato à limpeza: PR mergeado ou fechado, ou worktree que o git já considera órfão.
    /// O principal e o bare nunca entram (não são removíveis). Lock manual também protege,
    /// mas lock de ferramenta (supacode e afins) é só bookkeeping: entra, e a limpeza destrava antes.
    /// </summary>
    public bool IsCompleted =>
        !Worktree.IsMain
        && !Worktree.IsBare
        && (!Worktree.IsLocked || Worktree.IsToolLock)
        && (Worktree.IsPrunable || (_pullRequest is not null && !_pullRequest.IsOpen));

    /// <summary>Por que esta linha entrou na limpeza — usado no diálogo de confirmação.</summary>
    public string CompletionReason
    {
        get
        {
            if (Worktree.IsPrunable) return "órfão (a pasta não existe mais)";
            if (_pullRequest is null) return string.Empty;
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
            RefreshBadges();
        }
    }

    /// <summary>Ícones do estado local do worktree (coluna ESTADO).</summary>
    public IReadOnlyList<StatusBadge> GitBadges
    {
        get
        {
            var badges = new List<StatusBadge>();

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
                    + (_status.HasUncommittedChanges ? " Mas há alterações não commitadas: o git vai recusar." : string.Empty)));

            return badges;
        }
    }

    /// <summary>Ícones do PR (coluna PR), ao lado do número.</summary>
    public IReadOnlyList<StatusBadge> PullRequestBadges
    {
        get
        {
            var badges = new List<StatusBadge>();

            // Sem PR a coluna fica vazia — nada de marcador de ausência.
            if (_pullRequest is not { } pullRequest) return badges;

            badges.Add(pullRequest.State.ToUpperInvariant() switch
            {
                "MERGED" => new StatusBadge(BadgeKind.PrMerged, $"PR #{pullRequest.Number} mergeado."),
                "CLOSED" => new StatusBadge(BadgeKind.PrClosed, $"PR #{pullRequest.Number} fechado sem merge."),
                _ => pullRequest.IsDraft
                    ? new StatusBadge(BadgeKind.PrDraft, $"PR #{pullRequest.Number} aberto como rascunho.")
                    : new StatusBadge(BadgeKind.PrOpen, $"PR #{pullRequest.Number} aberto."),
            });

            // Checks, review e mergeabilidade só fazem sentido enquanto o PR está aberto.
            if (!pullRequest.IsOpen) return badges;

            if (pullRequest.HasConflicts)
                badges.Add(new StatusBadge(BadgeKind.NeedsRebase, "Conflito com a base — precisa rebase ou merge antes de integrar."));
            else if (pullRequest.IsBehindBase)
                badges.Add(new StatusBadge(BadgeKind.NeedsRebase, "A base andou desde que a branch saiu — o GitHub pede atualização."));

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
    }

    private void RefreshBadges()
    {
        RaisePropertyChanged(nameof(GitBadges));
        RaisePropertyChanged(nameof(PullRequestBadges));
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
        return (number.Length > 0 && pullRequest.Number.ToString().StartsWith(number, StringComparison.Ordinal))
               || Contains(pullRequest.Title);
    }

    public bool CanLaunch => Directory.Exists(Worktree.FullPath) && !Worktree.IsBare;

    /// <summary>O principal e o bare não são removíveis com git worktree remove.</summary>
    public bool CanRemove => !Worktree.IsMain && !Worktree.IsBare;

    /// <summary>Só dá para atualizar uma branch que tem upstream — senão não há de onde puxar.</summary>
    public bool CanUpdateBranch => CanLaunch && Worktree.Branch is not null && _status is { IsKnown: true, HasUpstream: true };

    /// <summary>
    /// Há sessão do Claude Code gravada para esta pasta — condição para o `claude --continue`
    /// ter o que retomar. Lido quando a linha é criada, a cada carregamento da lista.
    /// </summary>
    public bool HasClaudeSession { get; }

    public void RefreshTags()
    {
        RaisePropertyChanged(nameof(Tags));
        RaisePropertyChanged(nameof(TagsTooltip));
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
            + ". O git não o remove no prune e a limpeza do Hypertree também o ignora. "
            + "Destrave com git worktree unlock.",

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
