namespace Hypertree.ViewModels;

public sealed record LegendEntry(StatusBadge Badge, string Title, string Description)
{
    public string PathData => Badge.PathData;
    public string ColorHex => Badge.ColorHex;
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
        Entry(BadgeKind.Removable, "Pode ser removido",
            "Entra na limpeza: PR mergeado ou fechado, ou worktree órfão. Passar o mouse mostra o motivo."),
    };

    public IReadOnlyList<LegendEntry> PullRequestLegend { get; } = new[]
    {
        Entry(BadgeKind.PrOpen, "PR aberto", "Existe um pull request aberto para esta branch."),
        Entry(BadgeKind.PrDraft, "PR em rascunho", "O pull request está aberto como draft."),
        Entry(BadgeKind.PrMerged, "PR mergeado", "O pull request foi integrado."),
        Entry(BadgeKind.PrClosed, "PR fechado", "O pull request foi fechado sem merge."),
        Entry(BadgeKind.NeedsRebase, "Precisa atualizar",
            "Conflito com a base, ou a base andou desde que a branch saiu. Rebase ou merge resolve. Depende do gh reportar mergeable/mergeStateStatus — em repositório grande o GitHub às vezes devolve UNKNOWN e o ícone não aparece."),
        Entry(BadgeKind.ChecksPassing, "Checks passando", "Toda a CI do PR terminou com sucesso."),
        Entry(BadgeKind.ChecksFailing, "Checks falhando", "Algum check falhou. O tooltip lista os primeiros."),
        Entry(BadgeKind.ChecksPending, "Checks rodando", "A CI ainda não terminou."),
        Entry(BadgeKind.ReviewApproved, "Review aprovado", "O pull request recebeu aprovação."),
        Entry(BadgeKind.ReviewChangesRequested, "Mudanças solicitadas", "Um revisor pediu alterações."),
        Entry(BadgeKind.ReviewRequired, "Aguardando review", "O pull request ainda precisa de revisão."),
    };
}
