using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Hypercode.Services;
using Hypercode.ViewModels;

namespace Hypercode.Views;

/// <summary>
/// O menu de contexto do worktree: um só no código, para a lista de worktrees e para a issue do
/// grafo que já tem worktree (#148). Itens, rótulos, habilitação e confirmações moram aqui —
/// mudar um item muda nos dois lugares.
/// <para>
/// Age sobre as linhas que <c>targets</c> devolve na hora: na lista, a seleção inteira (com uma
/// linha, cada item segue o caminho de sempre; com várias, vira lote e termina num relatório);
/// no grafo, a linha do worktree da issue. Os diálogos abrem sobre a janela de <c>host</c> — a
/// principal ou a do grafo destacado.
/// </para>
/// </summary>
public sealed class WorktreeMenu
{
    private readonly Control _host;
    private readonly Func<RepositoryViewModel?> _repository;
    private readonly Func<IReadOnlyList<WorktreeRow>> _targets;

    /// <param name="extra">Itens do lugar, depois dos do worktree e de um separador (o grafo põe os da issue).</param>
    public WorktreeMenu(
        Control host,
        Func<RepositoryViewModel?> repository,
        Func<IReadOnlyList<WorktreeRow>> targets,
        IEnumerable<Control>? extra = null)
    {
        _host = host;
        _repository = repository;
        _targets = targets;

        // O tooltip do primeiro vem do Opening: o que o item faz ou, desabilitado, por quê.
        FocusTerminal = Item(OnFocusTerminalClick);
        ToolTip.SetShowOnDisabled(FocusTerminal, true);
        Launch = Item(OnOpenTerminalClick, "Abre uma janela nova, mesmo que já haja terminal aberto aqui.");
        Shell = Item(OnOpenShellClick, "Abre o terminal na pasta do worktree, sem rodar o comando.");
        ResumeClaude = Item(OnResumeClaudeClick, "Abre o terminal rodando claude --continue: volta à última conversa deste worktree.");
        Reveal = Item(OnRevealClick);
        OpenPullRequest = Item(OnOpenPullRequestClick);
        MarkSeen = Item(OnMarkSeenClick,
            "Tira o sino da linha: as mudanças do PR avisadas pelo monitoramento ficam como vistas. Abrir o PR no navegador faz o mesmo.");
        RerunFailedChecks = Item(OnRerunFailedChecksClick,
            "gh run rerun --failed em cada workflow run do GitHub Actions com job falho no último commit do PR: só os jobs que falharam voltam para a fila. Check de CI externo (status commit) não é reexecutado.");
        UpdateBranch = Item(OnUpdateBranchClick,
            "git pull --ff-only: traz os commits do upstream da própria branch. Se a branch divergiu, o git recusa e nada muda. Não traz a base do PR.");
        UpdateFromBase = Item(OnUpdateFromBaseClick,
            "Faz fetch da base do PR e a traz para dentro da branch, com merge ou rebase — você escolhe na hora. É o que resolve o ícone de base desatualizada. Uma linha por vez.");
        Lock = Item(OnLockClick,
            "git worktree lock, com um motivo opcional: o git passa a recusar remover e podar o worktree, e a limpeza o ignora.");
        Unlock = Item(OnUnlockClick, "git worktree unlock, depois de mostrar o worktree e a trava. Uma linha por vez.");
        Remove = Item(OnRemoveClick, "git worktree remove, com confirmação. A branch local não é tocada.");

        var items = new List<Control>
        {
            FocusTerminal, Launch, Shell, ResumeClaude,
            new Separator(),
            Reveal, OpenPullRequest, MarkSeen, RerunFailedChecks,
            new Separator(),
            UpdateBranch, UpdateFromBase, Lock, Unlock, Remove,
        };

        if (extra?.ToList() is { Count: > 0 } placeItems)
        {
            items.Add(new Separator());
            items.AddRange(placeItems);
        }

        Menu = new ContextMenu { ItemsSource = items };
        Menu.Opening += OnOpening;
    }

    public ContextMenu Menu { get; }

    public MenuItem FocusTerminal { get; }
    public MenuItem Launch { get; }
    public MenuItem Shell { get; }
    public MenuItem ResumeClaude { get; }
    public MenuItem Reveal { get; }
    public MenuItem OpenPullRequest { get; }
    public MenuItem MarkSeen { get; }
    public MenuItem RerunFailedChecks { get; }
    public MenuItem UpdateBranch { get; }
    public MenuItem UpdateFromBase { get; }
    public MenuItem Lock { get; }
    public MenuItem Unlock { get; }
    public MenuItem Remove { get; }

    private static MenuItem Item(EventHandler<RoutedEventArgs> click, string? tip = null)
    {
        var item = new MenuItem();
        item.Click += click;
        if (tip is not null) ToolTip.SetTip(item, tip);
        return item;
    }

    private RepositoryViewModel? ViewModel => _repository();

    /// <summary>Linhas-alvo do menu, na ordem da lista.</summary>
    private IReadOnlyList<WorktreeRow> MenuTargets() => _targets();

    /// <summary>A janela em que o menu está: a principal ou a do grafo destacado.</summary>
    private Window Owner => TopLevel.GetTopLevel(_host) as Window
                            ?? throw new InvalidOperationException("O menu do worktree precisa de uma janela.");

    private void OnOpening(object? sender, CancelEventArgs e) => e.Cancel = !Prepare();

    /// <summary>
    /// Configura para as linhas-alvo de agora; false se não há nenhuma. O Opening chama sozinho
    /// quando o menu está preso a um controle; quem abre com <see cref="ContextMenu.Open(Control)"/> chama antes.
    /// </summary>
    public bool Prepare()
    {
        var rows = MenuTargets();
        if (rows.Count == 0 || ViewModel is null) return false;

        Configure(rows);
        return true;
    }

    /// <summary>Rótulos, habilitação e visibilidade para <paramref name="rows"/>.</summary>
    public void Configure(IReadOnlyList<WorktreeRow> rows)
    {
        // Ir para o terminal é de uma linha só: com várias, qual traria para a frente?
        FocusTerminal.Header = "Ir para o terminal aberto";
        FocusTerminal.IsEnabled = rows is [{ CanFocusTerminal: true }];
        ToolTip.SetTip(FocusTerminal, rows switch
        {
            [{ FocusTerminalUnavailableReason: { } reason }] => reason,
            [_] => "Traz para a frente a sessão do iTerm2 ou do Terminal aberta neste worktree — a mais recente, se houver várias. É o que o duplo-clique faz quando há uma.",
            _ => "Vale para uma linha só: com várias selecionadas, qual traria para a frente?",
        });
        Configure(Launch, $"Abrir no {TerminalLauncher.TerminalName} rodando o comando", rows, row => row.CanLaunch);
        Configure(Shell, "Abrir o terminal", rows, row => row.CanLaunch);
        Configure(ResumeClaude, "Retomar a sessão do claude", rows, row => row.HasClaudeSession);
        Configure(Reveal, "Revelar no Finder", rows, _ => true);
        Configure(OpenPullRequest, "Abrir PR no navegador", rows, row => row.HasPullRequest);
        Configure(MarkSeen, "Marcar como visto", rows, row => row.HasPullRequestChanges);
        MarkSeen.IsVisible = MarkSeen.IsEnabled;
        Configure(RerunFailedChecks, "Rodar novamente os checks que falharam", rows, row => row.CanRerunFailedChecks);
        RerunFailedChecks.IsVisible = RerunFailedChecks.IsEnabled;
        Configure(UpdateBranch, "Puxar do remoto (pull)", rows, row => row.CanUpdateBranch);
        Configure(Remove, rows.Count > 1 ? "Apagar os worktrees…" : "Apagar o worktree…", rows, row => row.CanRemove);

        // Cada item só aparece se alguma linha estiver no estado que ele muda: travar some
        // quando tudo já está travado, destravar some quando nada está.
        Configure(Lock, rows.Count > 1 ? "Travar os worktrees…" : "Travar o worktree…", rows, row => row.CanLock);
        Lock.IsVisible = Lock.IsEnabled;

        // Destravar passa por uma confirmação com os dados daquela trava: não vira lote.
        Unlock.Header = "Destravar o worktree…";
        Unlock.IsVisible = rows.Any(row => row.CanUnlock);
        Unlock.IsEnabled = rows is [{ CanUnlock: true }];

        // Merge ou rebase é uma escolha por worktree, feita num diálogo: não vira lote.
        UpdateFromBase.Header = "Atualizar a partir da base…";
        UpdateFromBase.IsEnabled = rows.Count == 1 && rows[0].CanUpdateFromBase;
    }

    /// <summary>Com várias linhas, o rótulo diz em quantas a ação vale; basta uma para habilitar.</summary>
    private static void Configure(MenuItem item, string label, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports)
    {
        var count = rows.Count(supports);
        item.Header = rows.Count > 1 ? $"{label} ({count})" : label;
        item.IsEnabled = count > 0;
    }

    // Relatório só quando algo falhou ou ficou de fora; o sucesso fica no rodapé, que o
    // ViewModel já preencheu com o Summary — como no caminho de um worktree só.
    private async Task ShowBatchReportAsync(BatchOutcome outcome)
    {
        if (!outcome.HasProblems) return;

        await ConfirmWindow.Notice(
            outcome.Action,
            outcome.Summary,
            outcome.Report).ShowDialog<bool>(Owner);
    }

    /// <summary>Uma janela por worktree: acima do limite, pergunta antes de abrir todas.</summary>
    private async Task<bool> ConfirmWindowsAsync(string what, IReadOnlyList<WorktreeRow> rows, Func<WorktreeRow, bool> supports)
    {
        var targets = rows.Where(supports).ToList();
        if (targets.Count <= RepositoryViewModel.WindowConfirmationThreshold) return true;

        return await new ConfirmWindow(
            what,
            $"Abrir {targets.Count} janelas de uma vez?",
            string.Join("\n", targets.Select(row => $"{row.Name}  —  {row.Branch}")),
            $"Abrir {targets.Count}").ShowDialog<bool>(Owner);
    }

    /// <summary>Abrir janelas em lote, perguntando antes se forem muitas.</summary>
    private async Task OpenManyAsync(
        string what,
        IReadOnlyList<WorktreeRow> rows,
        Func<WorktreeRow, bool> supports,
        Func<IReadOnlyList<WorktreeRow>, Task<BatchOutcome>> open)
    {
        if (!await ConfirmWindowsAsync(what, rows, supports)) return;

        await ShowBatchReportAsync(await open(rows));
    }

    private async void OnOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.LaunchAsync(rows[0]);
        else await OpenManyAsync($"Abrir no {TerminalLauncher.TerminalName}", rows, row => row.CanLaunch, viewModel.LaunchManyAsync);
    }

    private async void OnFocusTerminalClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && MenuTargets() is [var row]) await viewModel.FocusTerminalAsync(row);
    }

    private async void OnOpenShellClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.OpenShellAsync(rows[0]);
        else await OpenManyAsync("Abrir o terminal", rows, row => row.CanLaunch, viewModel.OpenShellManyAsync);
    }

    private async void OnResumeClaudeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.ResumeClaudeAsync(rows[0]);
        else await OpenManyAsync("Retomar a sessão do claude", rows, row => row.HasClaudeSession, viewModel.ResumeClaudeManyAsync);
    }

    private async void OnUpdateBranchClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1)
        {
            await viewModel.UpdateBranchAsync(rows[0]);
            return;
        }

        await ShowBatchReportAsync(await viewModel.UpdateBranchesAsync(rows));
    }

    // Fetch e checagens primeiro, para o diálogo mostrar a base e quantos commits vêm; só
    // então a escolha entre merge e rebase. Recusa e conflito viram diálogo, não só rodapé.
    private async void OnUpdateFromBaseClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || MenuTargets() is not [var row]) return;
        if (!row.CanUpdateFromBase) return;

        var plan = await viewModel.PrepareBaseUpdateAsync(row);

        if (plan.Distance is not { } distance)
        {
            viewModel.StatusMessage = $"{row.Name} não foi atualizado";
            await ConfirmWindow.Notice(
                "Atualizar a partir da base",
                $"Não dá para atualizar {row.Name} agora.",
                plan.Error ?? string.Empty).ShowDialog<bool>(Owner);
            return;
        }

        if (distance.Behind == 0)
        {
            viewModel.StatusMessage = $"{row.Branch} já contém {distance.Ref} — nada a trazer";
            return;
        }

        var strategy = await new UpdateFromBaseWindow(row, distance).ShowDialog<BaseUpdateStrategy?>(Owner);
        if (strategy is not { } chosen)
        {
            viewModel.StatusMessage = $"Atualização de {row.Branch} cancelada";
            return;
        }

        var outcome = await viewModel.UpdateFromBaseAsync(row, distance, chosen);
        if (outcome.Details is null) return;

        await ConfirmWindow.Notice(
            "Atualizar a partir da base",
            outcome.Summary,
            outcome.Details).ShowDialog<bool>(Owner);
    }

    // Duas etapas: remove sem --force; se o git recusar (alteração não commitada, arquivo
    // não versionado), explica o motivo e só então oferece forçar — o que descarta o trabalho.
    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count > 1)
        {
            await RemoveManyAsync(viewModel, rows);
            return;
        }

        if (rows is not [var row] || !row.CanRemove) return;

        var confirmed = await new ConfirmWindow(
            "Apagar o worktree",
            $"Apagar o worktree {row.Name}? A branch local não é tocada, só o worktree.",
            RepositoryViewModel.BuildRemovalSummary(row),
            "Apagar",
            ConfirmStyle.Destructive).ShowDialog<bool>(Owner);

        if (!confirmed) return;

        var error = await viewModel.RemoveWorktreeAsync(row);
        if (error is null || row.Worktree.IsPrunable || row.Worktree.IsLocked) return;

        var forced = await new ConfirmWindow(
            "Forçar a remoção",
            $"O git recusou apagar {row.Name}. Forçar descarta de vez as alterações não commitadas e os arquivos não versionados da pasta.",
            $"{error}\n\n{row.FullPath}",
            "Forçar e apagar",
            ConfirmStyle.Destructive).ShowDialog<bool>(Owner);

        if (forced) await viewModel.RemoveWorktreeAsync(row, force: true);
    }

    // Em lote é uma confirmação só, sem a segunda etapa de forçar: quem o git recusar fica,
    // e o relatório diz por quê.
    private async Task RemoveManyAsync(RepositoryViewModel viewModel, IReadOnlyList<WorktreeRow> rows)
    {
        var targets = rows.Where(row => row.CanRemove).ToList();
        if (targets.Count == 0) return;

        var headline = $"Apagar {targets.Count} worktree(s)? A branch local não é tocada, só o worktree."
                       + (targets.Count < rows.Count ? $" O principal e o bare ficam de fora ({rows.Count - targets.Count})." : string.Empty);

        var confirmed = await new ConfirmWindow(
            "Apagar os worktrees",
            headline,
            RepositoryViewModel.BuildBatchRemovalSummary(targets),
            $"Apagar {targets.Count}",
            ConfirmStyle.Destructive).ShowDialog<bool>(Owner);

        if (!confirmed) return;

        await ShowBatchReportAsync(await viewModel.RemoveWorktreesAsync(rows));
    }

    // Um motivo só, digitado uma vez, vale para todas as linhas da seleção que aceitam a trava.
    private async void OnLockClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var targets = MenuTargets().Where(row => row.CanLock).ToList();
        if (targets.Count == 0) return;

        var headline = targets is [var single]
            ? $"Travar o worktree {single.Name}?"
            : $"Travar {targets.Count} worktrees?";

        var reason = await new LockWorktreeWindow(
            headline,
            targets.Count == 1 ? "Travar" : $"Travar {targets.Count}").ShowDialog<string?>(Owner);

        if (reason is null) return;

        if (targets is [var row])
        {
            if (await viewModel.LockWorktreeAsync(row, reason) is { } error)
                await ShowLockErrorAsync("Travar o worktree", $"O git recusou travar {row.Name}.", error, row);
            return;
        }

        await ShowBatchReportAsync(await viewModel.LockWorktreesAsync(MenuTargets(), reason));
    }

    // Sempre com confirmação, e com Cancelar como padrão: a trava pode ser de outra ferramenta.
    private async void OnUnlockClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || MenuTargets() is not [var row] || !row.CanUnlock) return;

        var confirmed = await new ConfirmWindow(
            "Destravar o worktree",
            row.Worktree.IsToolLock
                ? $"Destravar {row.Name}? A trava é do {row.Worktree.LockOwner}."
                : $"Destravar {row.Name}?",
            RepositoryViewModel.BuildUnlockSummary(row),
            "Destravar",
            ConfirmStyle.CancelIsDefault).ShowDialog<bool>(Owner);

        if (!confirmed) return;

        if (await viewModel.UnlockWorktreeAsync(row) is { } error)
            await ShowLockErrorAsync("Destravar o worktree", $"O git recusou destravar {row.Name}.", error, row);
    }

    private async Task ShowLockErrorAsync(string title, string headline, string error, WorktreeRow row)
        => await ConfirmWindow.Notice(title, headline, $"{error}\n\n{row.FullPath}").ShowDialog<bool>(Owner);

    private void OnMarkSeenClick(object? sender, RoutedEventArgs e)
        => ViewModel?.MarkPullRequestChangesSeen(MenuTargets());

    private async void OnRerunFailedChecksClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.RerunFailedChecksAsync(rows[0]);
        else await ShowBatchReportAsync(await viewModel.RerunFailedChecksManyAsync(rows));
    }

    private async void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.RevealAsync(rows[0]);
        else await ShowBatchReportAsync(await viewModel.RevealManyAsync(rows));
    }

    private async void OnOpenPullRequestClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        var rows = MenuTargets();
        if (rows.Count == 1) await viewModel.OpenPullRequestAsync(rows[0]);
        else await OpenManyAsync("Abrir PR no navegador", rows, row => row.HasPullRequest, viewModel.OpenPullRequestsAsync);
    }
}
