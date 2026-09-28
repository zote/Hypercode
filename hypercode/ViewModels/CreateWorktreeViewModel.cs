using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>
/// Diálogo "Novo worktree": branch nova a partir de uma base, a branch de um PR, ou a branch
/// de trabalho de uma issue (claude/issue-&lt;n&gt;-&lt;slug&gt;, sugerida pelo título).
/// A pasta é sugerida em &lt;repo&gt;.worktrees/&lt;branch&gt; até o usuário editá-la.
/// </summary>
public sealed class CreateWorktreeViewModel : ObservableObject, IDisposable
{
    private enum CreateMode { NewBranch, PullRequest, Issue }

    private readonly string _mainWorktreePath;
    private readonly bool _assignIssue;

    private CreateMode _mode;
    private string? _remote;
    private string _branchName = string.Empty;
    private string _baseRef = string.Empty;
    private string _pullRequestNumberText = string.Empty;
    private PullRequestHead? _pullRequest;
    private string? _pullRequestSummary;
    private string _issueNumberText = string.Empty;
    private IssueInfo? _issue;
    private string? _issueSummary;
    private string _issueBranchName = string.Empty;
    private string? _suggestedIssueBranch;
    private string? _issueBranchWarning;
    private bool _issueBranchExistsLocally;
    private string _worktreePath = string.Empty;
    private bool _pathEditedByUser;
    private bool _openTerminal;
    private string? _errorMessage;
    private string? _progressMessage;
    private bool _isBusy;
    private CancellationTokenSource? _lookupCancellation;
    private CancellationTokenSource? _issueLookupCancellation;
    private CancellationTokenSource? _branchCheckCancellation;

    public CreateWorktreeViewModel(string mainWorktreePath, bool openTerminal, string command, bool assignIssue)
    {
        _mainWorktreePath = mainWorktreePath;
        _openTerminal = openTerminal;
        _assignIssue = assignIssue;

        // Todo trabalho nasce de uma issue (AGENTS.md). Explícito, para não depender da ordem do enum.
        _mode = CreateMode.Issue;
        Command = command;

        var patterns = WorktreeCreator.ReadIncludePatterns(mainWorktreePath);
        IncludeSummary = patterns.Count == 0
            ? $"Sem {WorktreeCreator.IncludeFileName} no repositório — nenhum arquivo não versionado será copiado."
            : $"{WorktreeCreator.IncludeFileName}: copia os arquivos ignorados que casarem com "
              + string.Join(", ", patterns.Take(6))
              + (patterns.Count > 6 ? $" e mais {patterns.Count - 6}" : string.Empty) + ".";
    }

    /// <summary>Diálogo fechado: interrompe as consultas que ainda estiverem esperando o gh ou o git.</summary>
    public void Dispose()
    {
        foreach (var cancellation in new[] { _lookupCancellation, _issueLookupCancellation, _branchCheckCancellation })
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
        }

        _lookupCancellation = null;
        _issueLookupCancellation = null;
        _branchCheckCancellation = null;
    }

    public string Command { get; }

    public string WorktreesRoot => WorktreeCreator.WorktreesRoot(_mainWorktreePath);

    public string IncludeSummary { get; }

    public IReadOnlyList<string> Branches { get; private set; } = Array.Empty<string>();

    // Os RadioButtons desmarcam o anterior com false; só o true troca o modo.
    public bool IsNewBranchMode
    {
        get => _mode == CreateMode.NewBranch;
        set { if (value) Mode = CreateMode.NewBranch; }
    }

    public bool IsPullRequestMode
    {
        get => _mode == CreateMode.PullRequest;
        set { if (value) Mode = CreateMode.PullRequest; }
    }

    public bool IsIssueMode
    {
        get => _mode == CreateMode.Issue;
        set { if (value) Mode = CreateMode.Issue; }
    }

    private CreateMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            RaisePropertyChanged(nameof(IsNewBranchMode));
            RaisePropertyChanged(nameof(IsPullRequestMode));
            RaisePropertyChanged(nameof(IsIssueMode));
            ErrorMessage = null;
            SuggestPath();
            RaiseCanCreate();
        }
    }

    public string BranchName
    {
        get => _branchName;
        set
        {
            if (!SetProperty(ref _branchName, value ?? string.Empty)) return;
            SuggestPath();
            RaiseCanCreate();
        }
    }

    public string BaseRef
    {
        get => _baseRef;
        set
        {
            if (SetProperty(ref _baseRef, value ?? string.Empty)) RaiseCanCreate();
        }
    }

    /// <summary>Aceita 412, #412 ou a URL do PR.</summary>
    public string PullRequestNumberText
    {
        get => _pullRequestNumberText;
        set
        {
            if (!SetProperty(ref _pullRequestNumberText, value ?? string.Empty)) return;
            _ = LookupPullRequestAsync();
        }
    }

    public string? PullRequestSummary
    {
        get => _pullRequestSummary;
        private set => SetProperty(ref _pullRequestSummary, value);
    }

    /// <summary>Aceita 34, #34 ou a URL da issue.</summary>
    public string IssueNumberText
    {
        get => _issueNumberText;
        set
        {
            if (!SetProperty(ref _issueNumberText, value ?? string.Empty)) return;
            _ = LookupIssueAsync();
        }
    }

    public string? IssueSummary
    {
        get => _issueSummary;
        private set => SetProperty(ref _issueSummary, value);
    }

    /// <summary>
    /// Sugerida a partir da issue, mas editável. Enquanto o usuário não mexer, trocar o número
    /// troca a sugestão; depois de editada, fica como ele deixou.
    /// </summary>
    public string IssueBranchName
    {
        get => _issueBranchName;
        set
        {
            if (!SetProperty(ref _issueBranchName, value ?? string.Empty)) return;
            SuggestPath();
            RaiseCanCreate();
            _ = CheckIssueBranchAsync();
        }
    }

    public string? IssueBranchWarning
    {
        get => _issueBranchWarning;
        private set => SetProperty(ref _issueBranchWarning, value);
    }

    public string WorktreePath
    {
        get => _worktreePath;
        set
        {
            if (!SetProperty(ref _worktreePath, value ?? string.Empty)) return;

            // Apagar a pasta devolve a sugestão automática.
            _pathEditedByUser = _worktreePath.Trim().Length > 0;
            if (!_pathEditedByUser) SuggestPath();
            RaiseCanCreate();
        }
    }

    public bool OpenTerminal
    {
        get => _openTerminal;
        set => SetProperty(ref _openTerminal, value);
    }

    public string OpenTerminalLabel => $"Abrir {TerminalLauncher.TerminalName} no worktree novo rodando {Command}";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value)) RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public string? ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaisePropertyChanged(nameof(IsNotBusy));
            RaiseCanCreate();
        }
    }

    public bool IsNotBusy => !_isBusy;

    public bool CanCreate =>
        !_isBusy
        && _worktreePath.Trim().Length > 0
        && _mode switch
        {
            CreateMode.PullRequest => _pullRequest is not null,
            CreateMode.Issue => _issue is not null
                && !_issueBranchExistsLocally
                && _issueBranchName.Trim().Length > 0
                && _baseRef.Trim().Length > 0,
            _ => _branchName.Trim().Length > 0 && _baseRef.Trim().Length > 0,
        };

    private void RaiseCanCreate() => RaisePropertyChanged(nameof(CanCreate));

    /// <summary>Carrega as branches para o autocomplete e preenche a base padrão.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Branches = await GitService.ListBranchesAsync(_mainWorktreePath).ConfigureAwait(true);
            RaisePropertyChanged(nameof(Branches));

            _remote = await WorktreeCreator.PreferredRemoteAsync(_mainWorktreePath).ConfigureAwait(true);
            if (_baseRef.Length == 0
                && await GitService.ResolveDefaultBaseAsync(_mainWorktreePath, _remote ?? "origin").ConfigureAwait(true) is { } defaultBase)
            {
                BaseRef = defaultBase;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    /// <summary>Cria o worktree. Devolve null se falhou — o erro fica em <see cref="ErrorMessage"/>.</summary>
    public async Task<WorktreeCreationResult?> CreateAsync()
    {
        if (!CanCreate) return null;

        IsBusy = true;
        ErrorMessage = null;
        ProgressMessage = _mode == CreateMode.PullRequest
            ? $"Baixando o PR #{_pullRequest!.Number}…"
            : "Atualizando a base e criando…";

        try
        {
            var path = MainViewModel.ExpandHome(_worktreePath.Trim());

            // Para o git, a issue é só uma branch nova: ela alimenta as sugestões e a atribuição.
            var result = _mode switch
            {
                CreateMode.PullRequest => await WorktreeCreator
                    .CreateFromPullRequestAsync(_mainWorktreePath, _pullRequest!, path).ConfigureAwait(true),
                CreateMode.Issue => await WorktreeCreator
                    .CreateFromNewBranchAsync(_mainWorktreePath, _issueBranchName.Trim(), _baseRef.Trim(), path)
                    .ConfigureAwait(true),
                _ => await WorktreeCreator
                    .CreateFromNewBranchAsync(_mainWorktreePath, _branchName.Trim(), _baseRef.Trim(), path)
                    .ConfigureAwait(true),
            };

            if (_mode == CreateMode.Issue && _assignIssue && await AssignIssueAsync(_issue!).ConfigureAwait(true) is { } note)
                result = result with { Warnings = result.Warnings.Append(note).ToList() };

            return result;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return null;
        }
        finally
        {
            ProgressMessage = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Atribui a issue ao usuário do gh se ele ainda não estiver entre os assignees. O worktree
    /// já existe: falha aqui vira frase do rodapé, nunca erro do diálogo. Já atribuído, silêncio.
    /// </summary>
    private async Task<string?> AssignIssueAsync(IssueInfo issue)
    {
        ProgressMessage = $"Atribuindo a issue #{issue.Number}…";

        try
        {
            var login = await GitHubService.GetViewerLoginAsync(_mainWorktreePath).ConfigureAwait(true);
            if (issue.IsAssignedTo(login)) return null;

            await GitHubService.AssignIssueToViewerAsync(_mainWorktreePath, issue.Number).ConfigureAwait(true);
            return $"issue #{issue.Number} atribuída a {login}";
        }
        catch (Exception exception)
        {
            return $"não deu para atribuir a issue #{issue.Number}: {exception.Message}";
        }
    }

    private void SuggestPath()
    {
        if (_pathEditedByUser) return;

        var branch = _mode switch
        {
            CreateMode.PullRequest => _pullRequest?.HeadRefName,
            CreateMode.Issue => _issueBranchName.Trim(),
            _ => _branchName.Trim(),
        };
        var suggestion = string.IsNullOrEmpty(branch) ? string.Empty : WorktreeCreator.SuggestPath(_mainWorktreePath, branch);

        if (SetProperty(ref _worktreePath, suggestion, nameof(WorktreePath))) RaiseCanCreate();
    }

    /// <summary>Consulta o PR pelo gh, esperando o usuário parar de digitar.</summary>
    private async Task LookupPullRequestAsync()
    {
        _lookupCancellation?.Cancel();
        var cancellation = _lookupCancellation = new CancellationTokenSource();

        _pullRequest = null;
        SuggestPath();
        RaiseCanCreate();

        if (ParseNumber(_pullRequestNumberText) is not { } number)
        {
            PullRequestSummary = _pullRequestNumberText.Trim().Length == 0 ? null : "Informe o número do PR (ex.: 412 ou #412).";
            return;
        }

        try
        {
            await Task.Delay(400, cancellation.Token).ConfigureAwait(true);
            PullRequestSummary = $"Buscando o PR #{number}…";

            var pullRequest = await GitHubService
                .GetPullRequestHeadAsync(_mainWorktreePath, number, cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested) return;

            _pullRequest = pullRequest;
            PullRequestSummary =
                $"#{pullRequest.Number} · {pullRequest.Title}\nbranch {pullRequest.HeadRefName} · {pullRequest.State.ToLowerInvariant()}"
                + (pullRequest.IsCrossRepository ? " · de fork" : string.Empty);

            SuggestPath();
            RaiseCanCreate();
        }
        catch (OperationCanceledException)
        {
            // Digitou de novo — outra consulta assumiu.
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested) PullRequestSummary = exception.Message;
        }
    }

    /// <summary>Consulta a issue pelo gh, esperando o usuário parar de digitar.</summary>
    private async Task LookupIssueAsync()
    {
        _issueLookupCancellation?.Cancel();
        var cancellation = _issueLookupCancellation = new CancellationTokenSource();

        _issue = null;
        RaiseCanCreate();

        if (ParseNumber(_issueNumberText) is not { } number)
        {
            IssueSummary = _issueNumberText.Trim().Length == 0 ? null : "Informe o número da issue (ex.: 34 ou #34).";
            return;
        }

        try
        {
            await Task.Delay(400, cancellation.Token).ConfigureAwait(true);
            IssueSummary = $"Buscando a issue #{number}…";

            var issue = await GitHubService
                .GetIssueAsync(_mainWorktreePath, number, cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested) return;

            if (issue.IsPullRequest)
            {
                IssueSummary = $"#{issue.Number} é um pull request, não uma issue — use \"A partir de um PR\".";
                return;
            }

            _issue = issue;
            IssueSummary = $"#{issue.Number} · {issue.Title}\n"
                + (issue.IsClosed ? "issue fechada — confira se o número é esse mesmo" : "issue aberta");

            // Só substitui o que ainda é sugestão nossa; o nome que o usuário escreveu fica.
            var suggestion = WorktreeCreator.SuggestIssueBranch(issue.Number, issue.Title);
            if (_issueBranchName.Trim().Length == 0 || _issueBranchName == _suggestedIssueBranch)
            {
                _suggestedIssueBranch = suggestion;
                IssueBranchName = suggestion;
            }

            RaiseCanCreate();
        }
        catch (OperationCanceledException)
        {
            // Digitou de novo — outra consulta assumiu.
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested) IssueSummary = exception.Message;
        }
    }

    /// <summary>
    /// Avisa antes de criar se a branch já existe. Local bloqueia (o worktree add falharia);
    /// só no remoto é aviso — alguém pode já ter começado, mas criar ainda é possível.
    /// </summary>
    private async Task CheckIssueBranchAsync()
    {
        _branchCheckCancellation?.Cancel();
        var cancellation = _branchCheckCancellation = new CancellationTokenSource();

        _issueBranchExistsLocally = false;
        IssueBranchWarning = null;

        var branch = _issueBranchName.Trim();
        if (branch.Length == 0) return;

        try
        {
            await Task.Delay(250, cancellation.Token).ConfigureAwait(true);

            var local = await GitService
                .RefExistsAsync(_mainWorktreePath, $"refs/heads/{branch}", cancellation.Token)
                .ConfigureAwait(true);
            var remote = !local && _remote is not null && await GitService
                .RefExistsAsync(_mainWorktreePath, $"refs/remotes/{_remote}/{branch}", cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested) return;

            _issueBranchExistsLocally = local;
            IssueBranchWarning = local
                ? $"A branch '{branch}' já existe neste repositório. Escolha outro nome."
                : remote
                    ? $"Já existe {_remote}/{branch}: alguém pode ter começado essa issue. Se há PR, prefira \"A partir de um PR\"."
                    : null;
            RaiseCanCreate();
        }
        catch (OperationCanceledException)
        {
            // Editou de novo — outra checagem assumiu.
        }
    }

    private static int? ParseNumber(string text)
    {
        var value = text.Trim().TrimEnd('/');

        // https://github.com/dono/repo/pull/412 → 412; …/issues/34 → 34
        var slash = value.LastIndexOf('/');
        if (slash >= 0) value = value[(slash + 1)..];

        value = value.TrimStart('#');
        return int.TryParse(value, out var number) && number > 0 ? number : null;
    }
}
