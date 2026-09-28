using System.Text.RegularExpressions;
using Hypertree.ViewModels;

namespace Hypertree.Tests;

public partial class BadgeVisualsTests
{
    public static TheoryData<BadgeKind> AllKinds => new(Enum.GetValues<BadgeKind>());

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColor();

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void PathData_TodoIconeTemDesenhoComRegraNonZero(BadgeKind kind)
    {
        // Sem o "F1" o Avalonia assume EvenOdd e os Octicons saem com buracos.
        Assert.StartsWith("F1 M", BadgeVisuals.PathData(kind), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Tone_TodoIconeTemCorNosDoisTemas(BadgeKind kind)
    {
        var tone = BadgeVisuals.Tone(kind);

        Assert.Matches(HexColor(), tone.Light);
        Assert.Matches(HexColor(), tone.Dark);
        Assert.NotEqual(tone.Light, tone.Dark);
    }

    [Theory]
    [InlineData(BadgeKind.PrOpen, "Success")]
    [InlineData(BadgeKind.PrMerged, "Done")]
    [InlineData(BadgeKind.PrClosed, "Danger")]
    [InlineData(BadgeKind.PrDraft, "Muted")]
    [InlineData(BadgeKind.ChecksFailing, "Danger")]
    [InlineData(BadgeKind.ChecksPending, "Attention")]
    [InlineData(BadgeKind.Ahead, "Accent")]
    public void Tone_SegueOsPapeisDoPrimer(BadgeKind kind, string role)
    {
        var expected = role switch
        {
            "Success" => BadgeVisuals.Success,
            "Done" => BadgeVisuals.Done,
            "Danger" => BadgeVisuals.Danger,
            "Attention" => BadgeVisuals.Attention,
            "Accent" => BadgeVisuals.Accent,
            _ => BadgeVisuals.Muted,
        };

        Assert.Equal(expected, BadgeVisuals.Tone(kind));
    }

    [Fact]
    public void PathData_ConflitoERebaseUsamOMesmoAlertaComCoresDiferentes()
    {
        Assert.Equal(BadgeVisuals.PathData(BadgeKind.Conflicted), BadgeVisuals.PathData(BadgeKind.NeedsRebase));
        Assert.NotEqual(BadgeVisuals.Tone(BadgeKind.Conflicted), BadgeVisuals.Tone(BadgeKind.NeedsRebase));
    }

    [Fact]
    public void StatusBadge_ExpoeDesenhoECoresDoTipo()
    {
        var badge = new StatusBadge(BadgeKind.PrMerged, "mergeado");

        Assert.Equal(BadgeVisuals.PathData(BadgeKind.PrMerged), badge.PathData);
        Assert.Equal(BadgeVisuals.Done.Light, badge.LightColorHex);
        Assert.Equal(BadgeVisuals.Done.Dark, badge.DarkColorHex);
    }
}
