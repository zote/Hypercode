using System.Collections.ObjectModel;
using Hypercode.Services;
using Hypercode.ViewModels;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Recalcular com o mesmo conteúdo não avisa a interface: o aviso faz o ItemsControl recriar os
/// badges e o tooltip aberto sobre um deles fecha e reabre (#153).
/// </summary>
public sealed class TooltipStabilityTests
{
    private static readonly TerminalSession ITerm = new("/dev/ttys003", TerminalApp.ITerm2, TimeSpan.FromMinutes(1), "claude");

    private static WorktreeRow Row() => new(new WorktreeInfo { FullPath = "/dev/repo/feature", Branch = "feature" })
    {
        Status = new WorktreeStatus { IsKnown = true, HasUpstream = true, Ahead = 1 },
        Terminal = new TerminalPresence(new[] { ITerm }, new[] { "dotnet" }),
        PullRequest = new PullRequestInfo { Number = 7, State = "OPEN", Title = "Feature", HeadRefName = "feature" },
    };

    private static List<string> Notifications(WorktreeRow row)
    {
        var raised = new List<string>();
        row.PropertyChanged += (_, args) => raised.Add(args.PropertyName!);
        return raised;
    }

    [Fact]
    public void RelerOTerminalComOMesmoConteudoNaoAvisaOsBadges()
    {
        var row = Row();
        var badges = row.GitBadges;
        var raised = Notifications(row);

        // A leitura nova do lsof é outra instância, com listas novas — e o mesmo conteúdo.
        row.Terminal = new TerminalPresence(new[] { ITerm }, new[] { "dotnet" });

        Assert.DoesNotContain(nameof(WorktreeRow.GitBadges), raised);
        Assert.Same(badges, row.GitBadges);
    }

    [Fact]
    public void TerminalQueFechouAvisaNaHora()
    {
        var row = Row();
        var raised = Notifications(row);

        row.Terminal = TerminalPresence.None;

        Assert.Contains(nameof(WorktreeRow.GitBadges), raised);
        Assert.DoesNotContain(row.GitBadges, badge => badge.Kind == BadgeKind.TerminalOpen);
    }

    [Fact]
    public void PrRelidoIgualNaoAvisaBadgesNemTooltips()
    {
        var row = Row();
        var raised = Notifications(row);

        // O monitoramento traz sempre uma instância nova do mesmo PR.
        row.PullRequest = new PullRequestInfo { Number = 7, State = "OPEN", Title = "Feature", HeadRefName = "feature" };

        Assert.DoesNotContain(nameof(WorktreeRow.PullRequestBadges), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.GitBadges), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.PullRequestTooltip), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.TagsTooltip), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.Tags), raised);
    }

    [Fact]
    public void CheckQueQuebrouAvisaOsBadgesDoPr()
    {
        var row = Row();
        var raised = Notifications(row);

        row.PullRequest = new PullRequestInfo
        {
            Number = 7,
            State = "OPEN",
            Title = "Feature",
            HeadRefName = "feature",
            StatusCheckRollup = new List<CheckEntry> { new() { Name = "build", Conclusion = "FAILURE", Status = "COMPLETED" } },
        };

        Assert.Contains(nameof(WorktreeRow.PullRequestBadges), raised);
        Assert.Contains(row.PullRequestBadges, badge => badge.Kind == BadgeKind.ChecksFailing);
    }

    [Fact]
    public void PrMergeadoAvisaEtiquetasETooltips()
    {
        var row = Row();
        var raised = Notifications(row);

        row.PullRequest = new PullRequestInfo { Number = 7, State = "MERGED", Title = "Feature", HeadRefName = "feature" };

        Assert.Contains(nameof(WorktreeRow.Tags), raised);
        Assert.Contains(nameof(WorktreeRow.TagsTooltip), raised);
        Assert.Contains(nameof(WorktreeRow.PullRequestTooltip), raised);
        Assert.Equal("merged", row.Tags);
        Assert.Contains(row.GitBadges, badge => badge.Kind == BadgeKind.Removable);
    }

    [Fact]
    public void StatusQueMudaSemMudarOQueApareceNaoAvisaOsBadges()
    {
        var row = Row();
        var raised = Notifications(row);

        // Com o PR aberto, estar contido na base não muda nenhum badge.
        row.Status = new WorktreeStatus { IsKnown = true, HasUpstream = true, Ahead = 1, ContainedInBase = "origin/main" };

        Assert.Contains(nameof(WorktreeRow.Status), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.GitBadges), raised);
        Assert.DoesNotContain(nameof(WorktreeRow.PullRequestBadges), raised);
    }

    [Fact]
    public void SincronizarMantemOItemIgualETrocaSoODiferente()
    {
        var shown = new ObservableCollection<string> { "a", "b", "c" };
        var a = shown[0];
        var changes = 0;
        shown.CollectionChanged += (_, _) => changes++;

        CollectionSync.Apply(shown, new[] { "a", "B" });

        Assert.Equal(new[] { "a", "B" }, shown);
        Assert.Same(a, shown[0]);
        Assert.Equal(2, changes); // troca do "b" e saída do "c"
    }

    [Fact]
    public void SincronizarGrupoComMesmoCabecalhoMexeSoNosItens()
    {
        var shown = new ObservableCollection<RunnerGroupItem>();
        var idle = new RunnerItem("mini", "macOS · arm64", RunnerState.Idle, null, null);
        CollectionSync.ApplyGroups(
            shown, new[] { new RunnerGroupItem("g", "0 ocupados de 1 online", new[] { idle }) },
            group => group.Runners, (group, runners) => group with { Runners = runners },
            (a, b) => a.Name == b.Name && a.Summary == b.Summary);
        var group = shown.Single();
        var runner = group.Runners.Single();

        CollectionSync.ApplyGroups(
            shown, new[] { new RunnerGroupItem("g", "0 ocupados de 1 online", new[] { idle with { } }) },
            group => group.Runners, (group, runners) => group with { Runners = runners },
            (a, b) => a.Name == b.Name && a.Summary == b.Summary);

        Assert.Same(group, shown.Single());
        Assert.Same(runner, shown.Single().Runners.Single());
    }
}
