namespace Hypercode.ViewModels;

public enum BadgeKind
{
    Dirty,
    Conflicted,
    OperationPending,
    Diverged,
    Ahead,
    Behind,
    NeverPushed,
    UpstreamGone,
    Removable,
    PrOpen,
    PrDraft,
    PrMerged,
    PrClosed,
    ChecksPassing,
    ChecksFailing,
    ChecksPending,
    ReviewApproved,
    ReviewChangesRequested,
    ReviewRequired,
    NeedsRebase,
    PrChanged,

    /// <summary>Aba: algum PR do repositório com check falhando. Ponto, não ✕: ao lado do fechar da aba, um ✕ parece outro botão.</summary>
    RepositoryChecksFailing,
}

public sealed record StatusBadge(BadgeKind Kind, string Tooltip)
{
    public string IconKey => BadgeVisuals.IconKey(Kind);
    public string LightColorHex => BadgeVisuals.Tone(Kind).Light;
    public string DarkColorHex => BadgeVisuals.Tone(Kind).Dark;
}

/// <summary>Cor de um ícone nos temas claro e escuro, como o GitHub alterna.</summary>
public readonly record struct BadgeTone(string Light, string Dark);

/// <summary>
/// Ícone e cor de cada etiqueta. Só strings aqui de propósito: mantém a camada de
/// view-model livre do Avalonia e testável fora da interface. O ícone é a chave de um
/// recurso de Styles/Icons.axaml — os Octicons de 16px do GitHub
/// (https://primer.style/octicons/) — e as cores são os papéis de cor do Primer. Sempre
/// que o GitHub mostra a mesma informação, usa-se o mesmo ícone e a mesma cor que ele usa.
/// </summary>
public static class BadgeVisuals
{
    // fg.* do Primer (primitives) — o tom de texto/ícone de cada papel, em cada tema.
    public static readonly BadgeTone Success = new("#1A7F37", "#3FB950");
    public static readonly BadgeTone Danger = new("#D1242F", "#F85149");
    public static readonly BadgeTone Done = new("#8250DF", "#AB7DF8");
    public static readonly BadgeTone Attention = new("#9A6700", "#D29922");
    public static readonly BadgeTone Accent = new("#0969DA", "#4493F8");
    public static readonly BadgeTone Muted = new("#59636E", "#9198A1");

    public static string IconKey(BadgeKind kind) => kind switch
    {
        BadgeKind.Dirty => "Icon.DiffModified",
        BadgeKind.Conflicted => "Icon.Alert",
        BadgeKind.OperationPending => "Icon.Stop",
        BadgeKind.Diverged => "Icon.GitCompare",
        BadgeKind.Ahead => "Icon.ArrowUp",
        BadgeKind.Behind => "Icon.ArrowDown",
        BadgeKind.NeverPushed => "Icon.Upload",
        BadgeKind.UpstreamGone => "Icon.CloudOffline",
        BadgeKind.Removable => "Icon.Trash",
        BadgeKind.PrOpen => "Icon.GitPullRequest",
        BadgeKind.PrDraft => "Icon.GitPullRequestDraft",
        BadgeKind.PrMerged => "Icon.GitMerge",
        BadgeKind.PrClosed => "Icon.GitPullRequestClosed",
        BadgeKind.ChecksPassing => "Icon.Check",
        BadgeKind.ChecksFailing => "Icon.X",
        BadgeKind.ChecksPending => "Icon.DotFill",
        BadgeKind.ReviewApproved => "Icon.CheckCircle",
        BadgeKind.ReviewChangesRequested => "Icon.FileDiff",
        BadgeKind.ReviewRequired => "Icon.CodeReview",
        BadgeKind.NeedsRebase => "Icon.Alert",
        BadgeKind.PrChanged => "Icon.BellFill",
        BadgeKind.RepositoryChecksFailing => "Icon.DotFill",
        _ => "Icon.DotFill",
    };

    public static BadgeTone Tone(BadgeKind kind) => kind switch
    {
        BadgeKind.Dirty => Attention,
        BadgeKind.Conflicted => Danger,
        BadgeKind.OperationPending => Danger,
        BadgeKind.Diverged => Attention,
        BadgeKind.Ahead => Accent,
        BadgeKind.Behind => Accent,
        BadgeKind.NeverPushed => Muted,
        BadgeKind.UpstreamGone => Muted,
        BadgeKind.Removable => Muted,
        BadgeKind.PrOpen => Success,
        BadgeKind.PrDraft => Muted,
        BadgeKind.PrMerged => Done,
        BadgeKind.PrClosed => Danger,
        BadgeKind.ChecksPassing => Success,
        BadgeKind.ChecksFailing => Danger,
        BadgeKind.ChecksPending => Attention,
        BadgeKind.ReviewApproved => Success,
        BadgeKind.ReviewChangesRequested => Danger,
        BadgeKind.ReviewRequired => Muted,
        BadgeKind.NeedsRebase => Attention,
        BadgeKind.PrChanged => Accent,
        BadgeKind.RepositoryChecksFailing => Danger,
        _ => Muted,
    };
}
