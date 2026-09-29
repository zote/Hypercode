using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hypercode.ViewModels;
using Hypercode.Views;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// A cor que cada etiqueta de estado pinta de fato, na legenda da ajuda (que mostra todas),
/// em claro e escuro. São os fg.* do Primer: a mesma cor que o GitHub usa para a mesma
/// informação (#103).
/// </summary>
public sealed class BadgeColorsTests
{
    private static readonly Dictionary<string, (string Light, string Dark)> Primer = new()
    {
        ["Success"] = ("#1A7F37", "#3FB950"),
        ["Danger"] = ("#D1242F", "#F85149"),
        ["Done"] = ("#8250DF", "#AB7DF8"),
        ["Attention"] = ("#9A6700", "#D29922"),
        ["Accent"] = ("#0969DA", "#4493F8"),
        ["Muted"] = ("#59636E", "#9198A1"),
    };

    private static readonly Dictionary<BadgeKind, string> Esperado = new()
    {
        [BadgeKind.Dirty] = "Attention",
        [BadgeKind.Conflicted] = "Danger",
        [BadgeKind.OperationPending] = "Danger",
        [BadgeKind.Diverged] = "Attention",
        [BadgeKind.Ahead] = "Accent",
        [BadgeKind.Behind] = "Accent",
        [BadgeKind.NeverPushed] = "Muted",
        [BadgeKind.UpstreamGone] = "Muted",
        [BadgeKind.Removable] = "Muted",
        [BadgeKind.PrOpen] = "Success",
        [BadgeKind.PrDraft] = "Muted",
        [BadgeKind.PrMerged] = "Done",
        [BadgeKind.PrClosed] = "Danger",
        [BadgeKind.ChecksPassing] = "Success",
        [BadgeKind.ChecksFailing] = "Danger",
        [BadgeKind.ChecksPending] = "Attention",
        [BadgeKind.ReviewApproved] = "Success",
        [BadgeKind.ReviewChangesRequested] = "Danger",
        [BadgeKind.ReviewRequired] = "Muted",
        [BadgeKind.NeedsRebase] = "Attention",
        [BadgeKind.PrChanged] = "Accent",
        [BadgeKind.RepositoryChecksFailing] = "Danger",
    };

    [AvaloniaFact]
    public void CadaEtiquetaPintaACorDoPrimerNosDoisTemas()
    {
        var window = new HelpWindow();
        window.Show();
        var falhas = new List<string>();

        foreach (var (variante, escuro) in new[] { (ThemeVariant.Light, false), (ThemeVariant.Dark, true) })
        {
            window.RequestedThemeVariant = variante;
            Dispatcher.UIThread.RunJobs();

            var icones = window.GetVisualDescendants().OfType<PathIcon>()
                .Where(i => i.DataContext is LegendEntry)
                .ToList();
            Assert.Equal(Enum.GetValues<BadgeKind>().Order(), icones.Select(i => ((LegendEntry)i.DataContext!).Badge.Kind).Distinct().Order());

            foreach (var icone in icones)
            {
                var kind = ((LegendEntry)icone.DataContext!).Badge.Kind;
                var (claro, noEscuro) = Primer[Esperado[kind]];
                var esperado = Color.Parse(escuro ? noEscuro : claro);
                var cor = (icone.Foreground as ISolidColorBrush)?.Color;
                if (cor != esperado) falhas.Add($"{kind} em {variante}: {cor} (esperado {esperado})");
            }
        }

        window.Close();
        Assert.Empty(falhas);
    }
}
