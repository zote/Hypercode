using Hypertree.Services;

namespace Hypertree.ViewModels;

/// <summary>
/// Diálogo "Novo worktree": branch nova a partir de uma base, ou a branch de um PR.
/// A pasta é sugerida em &lt;repo&gt;.worktrees/&lt;branch&gt; até o usuário editá-la.
/// </summary>
public sealed class CreateWorktreeViewModel : ObservableObject
{
    private readonly string _mainWorktreePath;

    private bool _isPullRequestMode;
    private string _branchName = string.Empty;
    private string _baseRef = string.Empty;
    private string _pullRequestNumberText = string.Empty;
    private PullRequestHead? _pullRequest;
    private string? _pullRequestSummary;
    private string _worktreePath = string.Empty;
    private bool _pathEditedByUser;
    private bool _openTerminal;
    private string? _errorMessage;
    private string? _progressMessage;
    private bool _isBusy;
    private CancellationTokenSource? _lookupCancellation;

    public CreateWorktreeViewModel(string mainWorktreePath, bool openTerminal, string command)
    {
        _mainWorktreePath = mainWorktreePath;
        _openTerminal = openTerminal;
        Command = command;

        var patterns = WorktreeCreator.ReadIncludePatterns(mainWorktreePath);
        IncludeSummary = patterns.Count == 0
            ? $"Sem {WorktreeCreator.IncludeFileName} no repositório — nenhum arquivo não versionado será copiado."
            : $"{WorktreeCreator.IncludeFileName}: copia os arquivos ignorados que casarem com "
              + string.Join(", ", patterns.Take(6))
              + (patterns.Count > 6 ? $" e mais {patterns.Count - 6}" : string.Empty) + ".";
    }

    public string Command { get; }

    public string WorktreesRoot => WorktreeCreator.WorktreesRoot(_mainWorktreePath);

    public string IncludeSummary { get; }

    public IReadOnlyList<string> Branches { get; private set; } = Array.Empty<string>();

    public bool IsNewBranchMode
    {
        get => !_isPullRequestMode;
        set => IsPullRequestMode = !value;
    }

    public bool IsPullRequestMode
    {
        get => _isPullRequestMode;
        set
        {
            if (!SetProperty(ref _isPullRequestMode, value)) return;
            RaisePropertyChanged(nameof(IsNewBranchMode));
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
        && (_isPullRequestMode
            ? _pullRequest is not null
            : _branchName.Trim().Length > 0 && _baseRef.Trim().Length > 0);

    private void RaiseCanCreate() => RaisePropertyChanged(nameof(CanCreate));

    /// <summary>Carrega as branches para o autocomplete e preenche a base padrão.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Branches = await GitService.ListBranchesAsync(_mainWorktreePath).ConfigureAwait(true);
            RaisePropertyChanged(nameof(Branches));

            var remote = await WorktreeCreator.PreferredRemoteAsync(_mainWorktreePath).ConfigureAwait(true);
            if (_baseRef.Length == 0
                && await GitService.ResolveDefaultBaseAsync(_mainWorktreePath, remote ?? "origin").ConfigureAwait(true) is { } defaultBase)
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
        ProgressMessage = _isPullRequestMode ? $"Baixando o PR #{_pullRequest!.Number}…" : "Atualizando a base e criando…";

        try
        {
            var path = MainViewModel.ExpandHome(_worktreePath.Trim());

            return _isPullRequestMode
                ? await WorktreeCreator.CreateFromPullRequestAsync(_mainWorktreePath, _pullRequest!, path).ConfigureAwait(true)
                : await WorktreeCreator.CreateFromNewBranchAsync(
                    _mainWorktreePath, _branchName.Trim(), _baseRef.Trim(), path).ConfigureAwait(true);
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

    private void SuggestPath()
    {
        if (_pathEditedByUser) return;

        var branch = _isPullRequestMode ? _pullRequest?.HeadRefName : _branchName.Trim();
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

        if (ParsePullRequestNumber(_pullRequestNumberText) is not { } number)
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

    private static int? ParsePullRequestNumber(string text)
    {
        var value = text.Trim().TrimEnd('/');

        // https://github.com/dono/repo/pull/412 → 412
        var slash = value.LastIndexOf('/');
        if (slash >= 0) value = value[(slash + 1)..];

        value = value.TrimStart('#');
        return int.TryParse(value, out var number) && number > 0 ? number : null;
    }
}
