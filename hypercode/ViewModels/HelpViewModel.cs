namespace Hypercode.ViewModels;

public sealed record LegendEntry(StatusBadge Badge, string Title, string Description)
{
    public string PathData => Badge.PathData;
    public string LightColorHex => Badge.LightColorHex;
    public string DarkColorHex => Badge.DarkColorHex;
}

/// <summary>
/// Legenda da janela de ajuda. Usa os mesmos desenhos da lista, então nunca
/// sai de sincronia com o que aparece nas linhas.
/// </summary>
public sealed class HelpViewModel
{
    private static LegendEntry Entry(BadgeKind kind, string title, string description)
        => new(new StatusBadge(kind, title), title, description);

    public IReadOnlyList<LegendEntry> WorktreeLegend { get; } = new[]
    {
        Entry(BadgeKind.Dirty, "Alterações não commitadas",
            "Há arquivos modificados, novos ou removidos na árvore de trabalho. A limpeza não remove um worktree assim: o git recusa sem --force."),
        Entry(BadgeKind.Conflicted, "Conflito não resolvido",
            "Existem caminhos em estado unmerged. Resolva os arquivos e faça o commit antes de seguir."),
        Entry(BadgeKind.OperationPending, "Operação git pausada",
            "Um merge, rebase, cherry-pick, revert ou bisect parou no meio neste worktree. Conclua com --continue ou desfaça com --abort."),
        Entry(BadgeKind.Ahead, "Falta push",
            "A branch tem commits que o upstream não tem."),
        Entry(BadgeKind.Behind, "Falta pull",
            "O upstream tem commits que a branch local não tem."),
        Entry(BadgeKind.Diverged, "Divergiu",
            "Os dois lados andaram: há commit local e commit remoto que o outro não tem. Precisa merge ou rebase."),
        Entry(BadgeKind.NeverPushed, "Sem upstream",
            "A branch nunca foi pushada — não existe remota correspondente."),
        Entry(BadgeKind.UpstreamGone, "Branch remota apagada",
            "A branch rastreia uma remota que não existe mais (upstream \"gone\"), normalmente porque o PR foi mergeado e o GitHub apagou a branch. Aparece depois do fetch --prune que o monitoramento faz. Entra na limpeza só se o HEAD já estiver contido na base remota (origin/HEAD); caso contrário, remova pelo menu."),
        Entry(BadgeKind.Removable, "Pode ser removido",
            "Entra na limpeza: PR mergeado ou fechado, worktree órfão, ou branch remota apagada já contida na base. Passar o mouse mostra o motivo."),
    };

    public IReadOnlyList<LegendEntry> PullRequestLegend { get; } = new[]
    {
        Entry(BadgeKind.PrChanged, "PR mudou",
            "O monitoramento viu uma transição desde a última olhada: checks passaram ou falharam, review aprovado ou pedindo mudanças, PR mergeado ou fechado, conflito ou base à frente. O tooltip lista o quê. Some com \"Marcar como visto\" ou abrindo o PR no navegador."),
        Entry(BadgeKind.PrOpen, "PR aberto", "Existe um pull request aberto para esta branch."),
        Entry(BadgeKind.PrDraft, "PR em rascunho", "O pull request está aberto como draft."),
        Entry(BadgeKind.PrMerged, "PR mergeado", "O pull request foi integrado."),
        Entry(BadgeKind.PrClosed, "PR fechado", "O pull request foi fechado sem merge."),
        Entry(BadgeKind.NeedsRebase, "Precisa atualizar a partir da base",
            "Conflito com a base, ou a base do PR andou desde que a branch saiu. Resolve com \"Atualizar a partir da base…\" no menu de contexto (merge ou rebase da base); \"Puxar do remoto\" não resolve, porque traz o upstream da própria branch. Depende do gh reportar mergeable/mergeStateStatus — em repositório grande o GitHub às vezes devolve UNKNOWN e o ícone não aparece."),
        Entry(BadgeKind.ChecksPassing, "Checks passando", "Toda a CI do PR terminou com sucesso."),
        Entry(BadgeKind.ChecksFailing, "Checks falhando", "Algum check falhou. O tooltip lista os primeiros."),
        Entry(BadgeKind.ChecksPending, "Checks rodando", "A CI ainda não terminou."),
        Entry(BadgeKind.ReviewApproved, "Review aprovado", "O pull request recebeu aprovação."),
        Entry(BadgeKind.ReviewChangesRequested, "Mudanças solicitadas", "Um revisor pediu alterações."),
        Entry(BadgeKind.ReviewRequired, "Aguardando review", "O pull request ainda precisa de revisão."),
    };

    public IReadOnlyList<LegendEntry> TabLegend { get; } = new[]
    {
        Entry(BadgeKind.RepositoryChecksFailing, "CI falhando no repositório",
            "Algum PR aberto do repositório está com check falhando. O tooltip da aba diz quais."),
        Entry(BadgeKind.Removable, "Concluídos",
            "Quantos worktrees do repositório estão prontos para a limpeza — o mesmo número do Limpar concluídos daquela aba."),
        Entry(BadgeKind.PrChanged, "Novidade em segundo plano",
            "O monitoramento viu um PR do repositório mudar enquanto a aba estava atrás. Some ao trazer a aba para a frente; o sino da linha continua até marcar como visto."),
    };
}
