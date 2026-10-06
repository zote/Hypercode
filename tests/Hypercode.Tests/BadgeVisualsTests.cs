using Hypercode.ViewModels;

namespace Hypercode.Tests;

// As cores de verdade (hex nos dois temas) são conferidas no BadgeColorsTests, contra os
// brushes do Tokens.axaml. Aqui fica só o mapeamento de cada tipo para o papel e o ícone.
public class BadgeVisualsTests
{
    [Theory]
    [InlineData(BadgeKind.PrOpen, BadgeVisuals.Success)]
    [InlineData(BadgeKind.PrMerged, BadgeVisuals.Done)]
    [InlineData(BadgeKind.PrClosed, BadgeVisuals.Danger)]
    [InlineData(BadgeKind.PrDraft, BadgeVisuals.Muted)]
    [InlineData(BadgeKind.ChecksFailing, BadgeVisuals.Danger)]
    [InlineData(BadgeKind.ChecksPending, BadgeVisuals.Attention)]
    [InlineData(BadgeKind.Ahead, BadgeVisuals.Accent)]
    public void Tone_SegueOsPapeisDoPrimer(BadgeKind kind, string brushKey)
    {
        Assert.Equal(brushKey, BadgeVisuals.Tone(kind));
    }

    [Fact]
    public void IconKey_ConflitoERebaseUsamOMesmoAlertaComCoresDiferentes()
    {
        Assert.Equal(BadgeVisuals.IconKey(BadgeKind.Conflicted), BadgeVisuals.IconKey(BadgeKind.NeedsRebase));
        Assert.NotEqual(BadgeVisuals.Tone(BadgeKind.Conflicted), BadgeVisuals.Tone(BadgeKind.NeedsRebase));
    }

    [Fact]
    public void StatusBadge_ExpoeIconeECorDoTipo()
    {
        var badge = new StatusBadge(BadgeKind.PrMerged, "mergeado");

        Assert.Equal(BadgeVisuals.IconKey(BadgeKind.PrMerged), badge.IconKey);
        Assert.Equal(BadgeVisuals.Done, badge.BrushKey);
    }
}
