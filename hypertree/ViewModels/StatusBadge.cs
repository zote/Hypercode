namespace Hypertree.ViewModels;

public enum BadgeKind
{
    Dirty,
    Conflicted,
    OperationPending,
    Diverged,
    Ahead,
    Behind,
    NeverPushed,
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
}

public sealed record StatusBadge(BadgeKind Kind, string Tooltip)
{
    public string PathData => BadgeVisuals.PathData(Kind);
    public string ColorHex => BadgeVisuals.ColorHex(Kind);
}

/// <summary>
/// Desenho e cor de cada ícone. Só strings aqui de propósito: mantém a camada de
/// view-model livre do Avalonia e testável fora da interface.
/// Todos os caminhos são desenhados numa caixa 16x16.
/// </summary>
public static class BadgeVisuals
{
    // Tons que continuam legíveis tanto no tema claro quanto no escuro.
    public const string Amber = "#D29922";
    public const string Red = "#E5534B";
    public const string Green = "#2DA44E";
    public const string Blue = "#4C8EDA";
    public const string Purple = "#A371F7";
    public const string Gray = "#8B949E";

    private const string CircleFill = "M 2.6 8 A 5.4 5.4 0 1 0 13.4 8 A 5.4 5.4 0 1 0 2.6 8 Z";

    private const string Ring =
        "F0 M 2.4 8 A 5.6 5.6 0 1 0 13.6 8 A 5.6 5.6 0 1 0 2.4 8 Z"
        + " M 4.6 8 A 3.4 3.4 0 1 0 11.4 8 A 3.4 3.4 0 1 0 4.6 8 Z";

    private const string DotRing =
        "F0 M 2.4 8 A 5.6 5.6 0 1 0 13.6 8 A 5.6 5.6 0 1 0 2.4 8 Z"
        + " M 4.6 8 A 3.4 3.4 0 1 0 11.4 8 A 3.4 3.4 0 1 0 4.6 8 Z"
        + " M 6.4 8 A 1.6 1.6 0 1 0 9.6 8 A 1.6 1.6 0 1 0 6.4 8 Z";

    private const string ArrowUp = "M 8 1.6 L 13.4 8 H 10.2 V 14.4 H 5.8 V 8 H 2.6 Z";
    private const string ArrowDown = "M 8 14.4 L 2.6 8 H 5.8 V 1.6 H 10.2 V 8 H 13.4 Z";

    private const string DivergedArrows =
        "M 4.8 1.6 L 8.4 6 H 6.2 V 9.2 H 3.4 V 6 H 1.2 Z"
        + " M 11.2 14.4 L 7.6 10 H 9.8 V 6.8 H 12.6 V 10 H 14.8 Z";

    private const string ArrowUpBar = "M 8 1 L 13 7 H 10 V 10.6 H 6 V 7 H 3 Z M 2.8 12.2 H 13.2 V 14.6 H 2.8 Z";
    private const string PauseBars = "M 4.8 2.6 H 7.1 V 13.4 H 4.8 Z M 8.9 2.6 H 11.2 V 13.4 H 8.9 Z";
    private const string Trash = "M 6.2 1.4 H 9.8 V 2.9 H 13.2 V 4.9 H 2.8 V 2.9 H 6.2 Z M 4.1 6.2 H 11.9 L 11.1 14.6 H 4.9 Z";
    private const string Check = "M 6.3 12.6 L 2.1 8.4 L 3.8 6.7 L 6.3 9.2 L 12.2 3.3 L 13.9 5 Z";

    private const string Cross =
        "M 4.2 2.7 L 8 6.5 L 11.8 2.7 L 13.3 4.2 L 9.5 8 L 13.3 11.8 L 11.8 13.3"
        + " L 8 9.5 L 4.2 13.3 L 2.7 11.8 L 6.5 8 L 2.7 4.2 Z";

    private const string Alert =
        "F0 M 8 1.4 L 15.4 14.6 H 0.6 Z M 7.1 5.8 H 8.9 V 10 H 7.1 Z M 7.1 11.2 H 8.9 V 13 H 7.1 Z";

    private const string BubbleFill = "M 1.6 2.2 H 14.4 V 11 H 8.8 L 5.6 14.4 V 11 H 1.6 Z";

    private const string BubbleAlert =
        "F0 M 1.6 2.2 H 14.4 V 11 H 8.8 L 5.6 14.4 V 11 H 1.6 Z"
        + " M 7.1 4 H 8.9 V 7.6 H 7.1 Z M 7.1 8.6 H 8.9 V 10 H 7.1 Z";

    private const string BubbleRing =
        "F0 M 1.6 2.2 H 14.4 V 11 H 8.8 L 5.6 14.4 V 11 H 1.6 Z M 3.6 4.2 H 12.4 V 9 H 3.6 Z";

    private const string Minus = "M 3 7 H 13 V 9 H 3 Z";

    public static string PathData(BadgeKind kind) => kind switch
    {
        BadgeKind.Dirty => CircleFill,
        BadgeKind.Conflicted => Alert,
        BadgeKind.OperationPending => PauseBars,
        BadgeKind.Diverged => DivergedArrows,
        BadgeKind.Ahead => ArrowUp,
        BadgeKind.Behind => ArrowDown,
        BadgeKind.NeverPushed => ArrowUpBar,
        BadgeKind.Removable => Trash,
        BadgeKind.PrOpen => Ring,
        BadgeKind.PrDraft => Ring,
        BadgeKind.PrMerged => CircleFill,
        BadgeKind.PrClosed => Cross,
        BadgeKind.ChecksPassing => Check,
        BadgeKind.ChecksFailing => Cross,
        BadgeKind.ChecksPending => DotRing,
        BadgeKind.ReviewApproved => BubbleFill,
        BadgeKind.ReviewChangesRequested => BubbleAlert,
        BadgeKind.ReviewRequired => BubbleRing,
        BadgeKind.NeedsRebase => Alert,
        _ => Minus,
    };

    public static string ColorHex(BadgeKind kind) => kind switch
    {
        BadgeKind.Dirty => Amber,
        BadgeKind.Conflicted => Red,
        BadgeKind.OperationPending => Red,
        BadgeKind.Diverged => Amber,
        BadgeKind.Ahead => Blue,
        BadgeKind.Behind => Blue,
        BadgeKind.NeverPushed => Gray,
        BadgeKind.Removable => Gray,
        BadgeKind.PrOpen => Green,
        BadgeKind.PrDraft => Gray,
        BadgeKind.PrMerged => Purple,
        BadgeKind.PrClosed => Gray,
        BadgeKind.ChecksPassing => Green,
        BadgeKind.ChecksFailing => Red,
        BadgeKind.ChecksPending => Amber,
        BadgeKind.ReviewApproved => Green,
        BadgeKind.ReviewChangesRequested => Red,
        BadgeKind.ReviewRequired => Gray,
        BadgeKind.NeedsRebase => Amber,
        _ => Gray,
    };
}
