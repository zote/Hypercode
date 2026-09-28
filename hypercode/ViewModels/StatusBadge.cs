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
}

public sealed record StatusBadge(BadgeKind Kind, string Tooltip)
{
    public string PathData => BadgeVisuals.PathData(Kind);
    public string LightColorHex => BadgeVisuals.Tone(Kind).Light;
    public string DarkColorHex => BadgeVisuals.Tone(Kind).Dark;
}

/// <summary>Cor de um ícone nos temas claro e escuro, como o GitHub alterna.</summary>
public readonly record struct BadgeTone(string Light, string Dark);

/// <summary>
/// Desenho e cor de cada ícone. Só strings aqui de propósito: mantém a camada de
/// view-model livre do Avalonia e testável fora da interface.
/// Os desenhos são os Octicons de 16px do GitHub (https://primer.style/octicons/),
/// copiados do pacote @primer/octicons 19.15.0, e as cores são os papéis de cor do
/// Primer. Sempre que o GitHub mostra a mesma informação, usa-se o mesmo ícone e a
/// mesma cor que ele usa.
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

    // Paths exatos de <nome>-16.svg. "F1" = NonZero, a regra de preenchimento padrão
    // do SVG — o Avalonia assume EvenOdd quando o prefixo não vem.

    private const string DiffModified =
        "F1 M13.25 1c.966 0 1.75.784 1.75 1.75v10.5A1.75 1.75 0 0 1 13.25 15H2.75A1.75 1.75 0 0 1 1 13.25V2.75C1 1.784 1.784 1 2.75 1ZM2.75 2.5a.25.25 0 0 0-.25.25v10.5c0 .138.112.25.25.25h10.5a.25.25 0 0 0 .25-.25V2.75a.25.25 0 0 0-.25-.25ZM8 10a2 2 0 1 1-.001-3.999A2 2 0 0 1 8 10Z";

    private const string Alert =
        "F1 M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z";

    private const string Stop =
        "F1 M4.47.22A.749.749 0 0 1 5 0h6c.199 0 .389.079.53.22l4.25 4.25c.141.14.22.331.22.53v6a.749.749 0 0 1-.22.53l-4.25 4.25A.749.749 0 0 1 11 16H5a.749.749 0 0 1-.53-.22L.22 11.53A.749.749 0 0 1 0 11V5c0-.199.079-.389.22-.53Zm.84 1.28L1.5 5.31v5.38l3.81 3.81h5.38l3.81-3.81V5.31L10.69 1.5ZM8 4a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 8 4Zm0 8a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z";

    private const string GitCompare =
        "F1 M9.573.677A.25.25 0 0 1 10 .854V2.5h1A2.5 2.5 0 0 1 13.5 5v5.628a2.251 2.251 0 1 1-1.5 0V5a1 1 0 0 0-1-1h-1v1.646a.25.25 0 0 1-.427.177L7.177 3.427a.25.25 0 0 1 0-.354ZM6 12v-1.646a.25.25 0 0 1 .427-.177l2.396 2.396a.25.25 0 0 1 0 .354l-2.396 2.396A.25.25 0 0 1 6 15.146V13.5H5A2.5 2.5 0 0 1 2.5 11V5.372a2.25 2.25 0 1 1 1.5 0V11a1 1 0 0 0 1 1ZM4 3.25a.75.75 0 1 0-1.5 0 .75.75 0 0 0 1.5 0ZM12.75 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Z";

    private const string ArrowUp =
        "F1 M3.47 7.78a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0l4.25 4.25a.751.751 0 0 1-.018 1.042.751.751 0 0 1-1.042.018L9 4.81v7.44a.75.75 0 0 1-1.5 0V4.81L4.53 7.78a.75.75 0 0 1-1.06 0Z";

    private const string ArrowDown =
        "F1 M13.03 8.22a.75.75 0 0 1 0 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L3.47 9.28a.751.751 0 0 1 .018-1.042.751.751 0 0 1 1.042-.018l2.97 2.97V3.75a.75.75 0 0 1 1.5 0v7.44l2.97-2.97a.75.75 0 0 1 1.06 0Z";

    private const string Upload =
        "F1 M2.75 14A1.75 1.75 0 0 1 1 12.25v-2.5a.75.75 0 0 1 1.5 0v2.5c0 .138.112.25.25.25h10.5a.25.25 0 0 0 .25-.25v-2.5a.75.75 0 0 1 1.5 0v2.5A1.75 1.75 0 0 1 13.25 14Z M11.78 4.72a.749.749 0 1 1-1.06 1.06L8.75 3.811V9.5a.75.75 0 0 1-1.5 0V3.811L5.28 5.78a.749.749 0 1 1-1.06-1.06l3.25-3.25a.749.749 0 0 1 1.06 0l3.25 3.25Z";

    private const string CloudOffline =
        "F1 M7.25 2c-.69 0-1.351.13-1.957.371a.75.75 0 1 0 .554 1.394c.43-.17.903-.265 1.403-.265a3.72 3.72 0 0 1 3.541 2.496.75.75 0 0 0 .709.504c1.676 0 3 1.324 3 3a3 3 0 0 1-.681 1.92.75.75 0 0 0 1.156.955A4.496 4.496 0 0 0 16 9.5a4.472 4.472 0 0 0-3.983-4.471A5.222 5.222 0 0 0 7.25 2ZM.72 1.72a.75.75 0 0 1 1.06 0l2.311 2.31c.03.025.056.052.08.08l8.531 8.532.035.034 2.043 2.044a.749.749 0 0 1-.326 1.275.749.749 0 0 1-.734-.215l-1.8-1.799a4.54 4.54 0 0 1-.42.019h-8A3.474 3.474 0 0 1 0 10.5c0-1.41.809-2.614 2.001-3.17a5.218 5.218 0 0 1 .646-2.622L.72 2.78a.75.75 0 0 1 0-1.06ZM3.5 7.25c.004.161.018.322.041.481a.75.75 0 0 1-.557.833c-.86.22-1.484.986-1.484 1.936 0 1.124.876 2 2 2h6.94L3.771 5.832A3.788 3.788 0 0 0 3.5 7.25Z";

    private const string BellFill =
        "F1 M8 16c.9 0 1.7-.6 1.9-1.5.1-.3-.1-.5-.4-.5h-3c-.3 0-.5.2-.4.5.2.9 1 1.5 1.9 1.5ZM3 5c0-2.8 2.2-5 5-5s5 2.2 5 5v3l1.7 2.6c.2.2.3.5.3.8 0 .8-.7 1.5-1.5 1.5h-11c-.8.1-1.5-.6-1.5-1.4 0-.3.1-.6.3-.8L3 8.1V5Z";

    private const string Trash =
        "F1 M11 1.75V3h2.25a.75.75 0 0 1 0 1.5H2.75a.75.75 0 0 1 0-1.5H5V1.75C5 .784 5.784 0 6.75 0h2.5C10.216 0 11 .784 11 1.75ZM4.496 6.675l.66 6.6a.25.25 0 0 0 .249.225h5.19a.25.25 0 0 0 .249-.225l.66-6.6a.75.75 0 0 1 1.492.149l-.66 6.6A1.748 1.748 0 0 1 10.595 15h-5.19a1.75 1.75 0 0 1-1.741-1.575l-.66-6.6a.75.75 0 1 1 1.492-.15ZM6.5 1.75V3h3V1.75a.25.25 0 0 0-.25-.25h-2.5a.25.25 0 0 0-.25.25Z";

    private const string GitPullRequest =
        "F1 M1.5 3.25a2.25 2.25 0 1 1 3 2.122v5.256a2.251 2.251 0 1 1-1.5 0V5.372A2.25 2.25 0 0 1 1.5 3.25Zm5.677-.177L9.573.677A.25.25 0 0 1 10 .854V2.5h1A2.5 2.5 0 0 1 13.5 5v5.628a2.251 2.251 0 1 1-1.5 0V5a1 1 0 0 0-1-1h-1v1.646a.25.25 0 0 1-.427.177L7.177 3.427a.25.25 0 0 1 0-.354ZM3.75 2.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm0 9.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm8.25.75a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0Z";

    private const string GitPullRequestDraft =
        "F1 M3.25 1A2.25 2.25 0 0 1 4 5.372v5.256a2.251 2.251 0 1 1-1.5 0V5.372A2.251 2.251 0 0 1 3.25 1Zm9.5 14a2.25 2.25 0 1 1 0-4.5 2.25 2.25 0 0 1 0 4.5ZM2.5 3.25a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0ZM3.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm9.5 0a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5ZM14 7.5a1.25 1.25 0 1 1-2.5 0 1.25 1.25 0 0 1 2.5 0Zm0-4.25a1.25 1.25 0 1 1-2.5 0 1.25 1.25 0 0 1 2.5 0Z";

    private const string GitMerge =
        "F1 M5.45 5.154A4.25 4.25 0 0 0 9.25 7.5h1.378a2.251 2.251 0 1 1 0 1.5H9.25A5.734 5.734 0 0 1 5 7.123v3.505a2.25 2.25 0 1 1-1.5 0V5.372a2.25 2.25 0 1 1 1.95-.218ZM4.25 13.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Zm8.5-4.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5ZM5 3.25a.75.75 0 1 0 0 .005V3.25Z";

    private const string GitPullRequestClosed =
        "F1 M3.25 1A2.25 2.25 0 0 1 4 5.372v5.256a2.251 2.251 0 1 1-1.5 0V5.372A2.251 2.251 0 0 1 3.25 1Zm9.5 5.5a.75.75 0 0 1 .75.75v3.378a2.251 2.251 0 1 1-1.5 0V7.25a.75.75 0 0 1 .75-.75Zm-2.03-5.273a.75.75 0 0 1 1.06 0l.97.97.97-.97a.748.748 0 0 1 1.265.332.75.75 0 0 1-.205.729l-.97.97.97.97a.751.751 0 0 1-.018 1.042.751.751 0 0 1-1.042.018l-.97-.97-.97.97a.749.749 0 0 1-1.275-.326.749.749 0 0 1 .215-.734l.97-.97-.97-.97a.75.75 0 0 1 0-1.06ZM2.5 3.25a.75.75 0 1 0 1.5 0 .75.75 0 0 0-1.5 0ZM3.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm9.5 0a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Z";

    private const string Check =
        "F1 M13.78 4.22a.75.75 0 0 1 0 1.06l-7.25 7.25a.75.75 0 0 1-1.06 0L2.22 9.28a.751.751 0 0 1 .018-1.042.751.751 0 0 1 1.042-.018L6 10.94l6.72-6.72a.75.75 0 0 1 1.06 0Z";

    private const string X =
        "F1 M3.72 3.72a.75.75 0 0 1 1.06 0L8 6.94l3.22-3.22a.749.749 0 0 1 1.275.326.749.749 0 0 1-.215.734L9.06 8l3.22 3.22a.749.749 0 0 1-.326 1.275.749.749 0 0 1-.734-.215L8 9.06l-3.22 3.22a.751.751 0 0 1-1.042-.018.751.751 0 0 1-.018-1.042L6.94 8 3.72 4.78a.75.75 0 0 1 0-1.06Z";

    private const string DotFill =
        "F1 M8 4a4 4 0 1 1 0 8 4 4 0 0 1 0-8Z";

    private const string CheckCircle =
        "F1 M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm1.5 0a6.5 6.5 0 1 0 13 0 6.5 6.5 0 0 0-13 0Zm10.28-1.72-4.5 4.5a.75.75 0 0 1-1.06 0l-2-2a.751.751 0 0 1 .018-1.042.751.751 0 0 1 1.042-.018l1.47 1.47 3.97-3.97a.751.751 0 0 1 1.042.018.751.751 0 0 1 .018 1.042Z";

    private const string FileDiff =
        "F1 M1 1.75C1 .784 1.784 0 2.75 0h7.586c.464 0 .909.184 1.237.513l2.914 2.914c.329.328.513.773.513 1.237v9.586A1.75 1.75 0 0 1 13.25 16H2.75A1.75 1.75 0 0 1 1 14.25Zm1.75-.25a.25.25 0 0 0-.25.25v12.5c0 .138.112.25.25.25h10.5a.25.25 0 0 0 .25-.25V4.664a.25.25 0 0 0-.073-.177l-2.914-2.914a.25.25 0 0 0-.177-.073ZM8 3.25a.75.75 0 0 1 .75.75v1.5h1.5a.75.75 0 0 1 0 1.5h-1.5v1.5a.75.75 0 0 1-1.5 0V7h-1.5a.75.75 0 0 1 0-1.5h1.5V4A.75.75 0 0 1 8 3.25Zm-3 8a.75.75 0 0 1 .75-.75h4.5a.75.75 0 0 1 0 1.5h-4.5a.75.75 0 0 1-.75-.75Z";

    private const string CodeReview =
        "F1 M1.75 1h12.5c.966 0 1.75.784 1.75 1.75v8.5A1.75 1.75 0 0 1 14.25 13H8.061l-2.574 2.573A1.458 1.458 0 0 1 3 14.543V13H1.75A1.75 1.75 0 0 1 0 11.25v-8.5C0 1.784.784 1 1.75 1ZM1.5 2.75v8.5c0 .138.112.25.25.25h2a.75.75 0 0 1 .75.75v2.19l2.72-2.72a.749.749 0 0 1 .53-.22h6.5a.25.25 0 0 0 .25-.25v-8.5a.25.25 0 0 0-.25-.25H1.75a.25.25 0 0 0-.25.25Zm5.28 1.72a.75.75 0 0 1 0 1.06L5.31 7l1.47 1.47a.751.751 0 0 1-.018 1.042.751.751 0 0 1-1.042.018l-2-2a.75.75 0 0 1 0-1.06l2-2a.75.75 0 0 1 1.06 0Zm2.44 0a.75.75 0 0 1 1.06 0l2 2a.75.75 0 0 1 0 1.06l-2 2a.751.751 0 0 1-1.042-.018.751.751 0 0 1-.018-1.042L10.69 7 9.22 5.53a.75.75 0 0 1 0-1.06Z";

    public static string PathData(BadgeKind kind) => kind switch
    {
        BadgeKind.Dirty => DiffModified,
        BadgeKind.Conflicted => Alert,
        BadgeKind.OperationPending => Stop,
        BadgeKind.Diverged => GitCompare,
        BadgeKind.Ahead => ArrowUp,
        BadgeKind.Behind => ArrowDown,
        BadgeKind.NeverPushed => Upload,
        BadgeKind.UpstreamGone => CloudOffline,
        BadgeKind.Removable => Trash,
        BadgeKind.PrOpen => GitPullRequest,
        BadgeKind.PrDraft => GitPullRequestDraft,
        BadgeKind.PrMerged => GitMerge,
        BadgeKind.PrClosed => GitPullRequestClosed,
        BadgeKind.ChecksPassing => Check,
        BadgeKind.ChecksFailing => X,
        BadgeKind.ChecksPending => DotFill,
        BadgeKind.ReviewApproved => CheckCircle,
        BadgeKind.ReviewChangesRequested => FileDiff,
        BadgeKind.ReviewRequired => CodeReview,
        BadgeKind.NeedsRebase => Alert,
        BadgeKind.PrChanged => BellFill,
        _ => DotFill,
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
        _ => Muted,
    };
}
