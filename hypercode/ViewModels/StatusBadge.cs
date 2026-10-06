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

    /// <summary>Sessão de terminal aberta dentro do worktree.</summary>
    TerminalOpen,

    /// <summary>Processo com a pasta aberta, mas sem terminal (um dotnet watch).</summary>
    ProcessOpen,

    /// <summary>Sessão de multiplexador (o zmx do supacode, tmux...) sem janela para onde ir.</summary>
    MultiplexerSession,

    /// <summary>Aba: algum PR do repositório com check falhando. Ponto, não ✕: ao lado do fechar da aba, um ✕ parece outro botão.</summary>
    RepositoryChecksFailing,
}

public sealed record StatusBadge(BadgeKind Kind, string Tooltip)
{
    public string IconKey => BadgeVisuals.IconKey(Kind);
    public string BrushKey => BadgeVisuals.Tone(Kind);
}

/// <summary>
/// Ícone e cor de cada etiqueta. Só strings aqui de propósito: mantém a camada de
/// view-model livre do Avalonia e testável fora da interface. O ícone é a chave de um
/// recurso de Styles/Icons.axaml — os Octicons de 16px do GitHub
/// (https://primer.style/octicons/) — e a cor é a chave de um token Brush.Badge.* de
/// Styles/Tokens.axaml, com os papéis de cor do Primer em claro e escuro. Sempre que o
/// GitHub mostra a mesma informação, usa-se o mesmo ícone e a mesma cor que ele usa.
/// </summary>
public static class BadgeVisuals
{
    public const string Success = "Brush.Badge.Success";
    public const string Danger = "Brush.Badge.Danger";
    public const string Done = "Brush.Badge.Done";
    public const string Attention = "Brush.Badge.Attention";
    public const string Accent = "Brush.Badge.Accent";
    public const string Muted = "Brush.Badge.Muted";

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
        BadgeKind.TerminalOpen => "Icon.Terminal",
        BadgeKind.ProcessOpen => "Icon.Terminal",
        BadgeKind.MultiplexerSession => "Icon.Columns",
        BadgeKind.RepositoryChecksFailing => "Icon.DotFill",
        _ => "Icon.DotFill",
    };

    public static string Tone(BadgeKind kind) => kind switch
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
        BadgeKind.TerminalOpen => Accent,
        BadgeKind.ProcessOpen => Muted,
        BadgeKind.MultiplexerSession => Muted,
        BadgeKind.RepositoryChecksFailing => Danger,
        _ => Muted,
    };
}
