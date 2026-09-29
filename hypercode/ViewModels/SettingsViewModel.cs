using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Hypercode.Services;

namespace Hypercode.ViewModels;

/// <summary>Uma opção do seletor do topo das configurações: o global ou um repositório aberto.</summary>
public sealed record SettingsScope(string Label, RepositoryViewModel? Repository)
{
    public override string ToString() => Label;
}

/// <summary>
/// A tela de configurações. No escopo global, cada campo é o padrão de todos os repositórios.
/// No escopo de um repositório, cada campo ganha "usar o global": marcado, o campo mostra o
/// global e fica travado; desmarcado, o repositório passa a ter o valor dele, que começa igual
/// ao global. Cada mudança grava na hora e as abas relêem o que vale para elas.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly string[] FieldProperties =
    {
        nameof(IsRepositoryScope), nameof(ScopeHint),
        nameof(Command), nameof(CommandUsesGlobal), nameof(CanEditCommand), nameof(IsCommandOverridden),
        nameof(MonitorProfileIndex), nameof(MonitorProfileUsesGlobal), nameof(CanEditMonitorProfile), nameof(IsMonitorProfileOverridden),
        nameof(NotifyPullRequestChanges), nameof(NotifyUsesGlobal), nameof(CanEditNotify), nameof(IsNotifyOverridden),
        nameof(AssignIssueOnCreate), nameof(AssignIssueUsesGlobal), nameof(CanEditAssignIssue), nameof(IsAssignIssueOverridden),
        nameof(AutoCleanup), nameof(AutoCleanupGraceMinutes), nameof(AutoCleanupUsesGlobal), nameof(CanEditAutoCleanup),
        nameof(CanEditAutoCleanupGrace), nameof(IsAutoCleanupOverridden), nameof(AutoCleanupWarning),
        nameof(WorktreesRoot), nameof(WorktreesRootUsesGlobal), nameof(CanEditWorktreesRoot), nameof(IsWorktreesRootOverridden),
        nameof(WorktreesRootPreview),
        nameof(KeepSlashes), nameof(KeepSlashesUsesGlobal), nameof(CanEditKeepSlashes), nameof(IsKeepSlashesOverridden),
    };

    private readonly MainViewModel _main;
    private SettingsScope _selectedScope;

    public SettingsViewModel(MainViewModel main, RepositoryViewModel? initialScope = null)
    {
        _main = main;
        _selectedScope = new SettingsScope("Global — todos os repositórios", null);
        RebuildScopes(initialScope);
        _main.Repositories.CollectionChanged += OnRepositoriesChanged;
    }

    /// <summary>A janela fechou: deixa de acompanhar as abas.</summary>
    public void Detach() => _main.Repositories.CollectionChanged -= OnRepositoriesChanged;

    public ObservableCollection<SettingsScope> Scopes { get; } = new();

    public SettingsScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            // O ComboBox manda null enquanto a lista é refeita.
            if (value is null || value == _selectedScope) return;
            _selectedScope = value;
            RaisePropertyChanged();
            RaiseFields();
        }
    }

    public bool IsRepositoryScope => Repository is not null;

    public string ScopeHint => Repository is { } repository
        ? $"Só para {repository.DisplayName}. O que ficar em \"usar o global\" segue o global."
        : "Padrão de todos os repositórios. Um repositório pode sobrescrever qualquer campo: escolha-o acima.";

    private RepositoryViewModel? Repository => _selectedScope.Repository;

    private Settings Global => _main.Settings;

    /// <summary>O override do repositório do escopo, se houver um e ele já existir.</summary>
    private RepositorySettings? Overrides => Repository is { } repository ? Global.OverridesFor(repository.RepositoryPath) : null;

    private EffectiveSettings Effective => EffectiveSettings.Resolve(Global, Repository?.RepositoryPath);

    // ── Comando ─────────────────────────────────────────────────────────────

    public string Command
    {
        get => Repository is null ? Global.Command : Overrides?.Command ?? Global.Command;
        set => Write(CommandUsesGlobal,
            () => Global.Command = value ?? string.Empty,
            overrides => overrides.Command = value ?? string.Empty);
    }

    public bool CommandUsesGlobal
    {
        get => Overrides?.Command is null;
        set => SetUsesGlobal(overrides => overrides.Command = value ? null : Global.Command);
    }

    public bool IsCommandOverridden => IsRepositoryScope && !CommandUsesGlobal;
    public bool CanEditCommand => !IsRepositoryScope || !CommandUsesGlobal;

    // ── Monitoramento ───────────────────────────────────────────────────────

    /// <summary>Índice do seletor: desligado, econômico, equilibrado, agressivo — a ordem de <see cref="MonitorProfile"/>.</summary>
    public int MonitorProfileIndex
    {
        get => (int)(Repository is null ? EffectiveSettings.ParseMonitorProfile(Global.MonitorProfile) : Effective.MonitorProfile);
        set
        {
            if (!Enum.IsDefined(typeof(MonitorProfile), value)) return;
            var profile = EffectiveSettings.FormatMonitorProfile((MonitorProfile)value);
            Write(MonitorProfileUsesGlobal, () => Global.MonitorProfile = profile, overrides => overrides.MonitorProfile = profile);
        }
    }

    public bool MonitorProfileUsesGlobal
    {
        get => Overrides?.MonitorProfile is null;
        set => SetUsesGlobal(overrides => overrides.MonitorProfile = value
            ? null
            : EffectiveSettings.FormatMonitorProfile(EffectiveSettings.ParseMonitorProfile(Global.MonitorProfile)));
    }

    public bool IsMonitorProfileOverridden => IsRepositoryScope && !MonitorProfileUsesGlobal;
    public bool CanEditMonitorProfile => !IsRepositoryScope || !MonitorProfileUsesGlobal;

    // ── Notificações ────────────────────────────────────────────────────────

    public bool NotifyPullRequestChanges
    {
        get => Repository is null ? Global.NotifyPullRequestChanges : Effective.NotifyPullRequestChanges;
        set => Write(NotifyUsesGlobal, () => Global.NotifyPullRequestChanges = value, overrides => overrides.NotifyPullRequestChanges = value);
    }

    public bool NotifyUsesGlobal
    {
        get => Overrides?.NotifyPullRequestChanges is null;
        set => SetUsesGlobal(overrides => overrides.NotifyPullRequestChanges = value ? null : Global.NotifyPullRequestChanges);
    }

    public bool IsNotifyOverridden => IsRepositoryScope && !NotifyUsesGlobal;
    public bool CanEditNotify => !IsRepositoryScope || !NotifyUsesGlobal;

    // ── Atribuição de issue ─────────────────────────────────────────────────

    public bool AssignIssueOnCreate
    {
        get => Repository is null ? Global.AssignIssueOnCreate : Effective.AssignIssueOnCreate;
        set => Write(AssignIssueUsesGlobal, () => Global.AssignIssueOnCreate = value, overrides => overrides.AssignIssueOnCreate = value);
    }

    public bool AssignIssueUsesGlobal
    {
        get => Overrides?.AssignIssueOnCreate is null;
        set => SetUsesGlobal(overrides => overrides.AssignIssueOnCreate = value ? null : Global.AssignIssueOnCreate);
    }

    public bool IsAssignIssueOverridden => IsRepositoryScope && !AssignIssueUsesGlobal;
    public bool CanEditAssignIssue => !IsRepositoryScope || !AssignIssueUsesGlobal;

    // ── Limpeza automática (ligada e carência, sobrescritas juntas) ─────────

    /// <summary>Ligar é só gravar: o aviso do que se aceita fica com a tela, que só chama isto depois do "Ativar mesmo assim".</summary>
    public bool AutoCleanup
    {
        get => Repository is null ? Global.AutoCleanup : Effective.AutoCleanup;
        set => Write(AutoCleanupUsesGlobal, () => Global.AutoCleanup = value, overrides => overrides.AutoCleanup = value);
    }

    /// <summary>Carência em minutos, como decimal? do NumericUpDown. Vazio volta ao padrão.</summary>
    public decimal? AutoCleanupGraceMinutes
    {
        get => Repository is null ? EffectiveSettings.NormalizeGrace(Global.AutoCleanupGraceMinutes) : Effective.AutoCleanupGraceMinutes;
        set
        {
            var minutes = value is { } number ? (int)Math.Clamp(number, 1, 1440) : AutoCleanupTracker.DefaultGraceMinutes;
            Write(AutoCleanupUsesGlobal, () => Global.AutoCleanupGraceMinutes = minutes, overrides => overrides.AutoCleanupGraceMinutes = minutes);
        }
    }

    public bool AutoCleanupUsesGlobal
    {
        get => Overrides?.AutoCleanup is null;
        set => SetUsesGlobal(overrides =>
        {
            overrides.AutoCleanup = value ? null : Global.AutoCleanup;
            overrides.AutoCleanupGraceMinutes = value ? null : EffectiveSettings.NormalizeGrace(Global.AutoCleanupGraceMinutes);
        });
    }

    public bool IsAutoCleanupOverridden => IsRepositoryScope && !AutoCleanupUsesGlobal;
    public bool CanEditAutoCleanup => !IsRepositoryScope || !AutoCleanupUsesGlobal;
    public bool CanEditAutoCleanupGrace => CanEditAutoCleanup && AutoCleanup;

    /// <summary>O aviso de quando se liga a limpeza, com a carência do escopo.</summary>
    public string AutoCleanupWarning => RepositoryViewModel.BuildAutoCleanupWarning((int)(AutoCleanupGraceMinutes ?? AutoCleanupTracker.DefaultGraceMinutes));

    // ── Pasta dos worktrees (raiz e barras, sobrescritas separadas) ────────

    /// <summary>Vazio é o padrão, &lt;repo&gt;.worktrees ao lado do repositório.</summary>
    public string WorktreesRoot
    {
        get => (Repository is null ? Global.WorktreesRoot : Overrides?.WorktreesRoot ?? Global.WorktreesRoot) ?? string.Empty;
        set => Write(WorktreesRootUsesGlobal,
            () => Global.WorktreesRoot = EffectiveSettings.NormalizeWorktreesRoot(value),
            overrides => overrides.WorktreesRoot = value?.Trim() ?? string.Empty);
    }

    public bool WorktreesRootUsesGlobal
    {
        get => Overrides?.WorktreesRoot is null;
        set => SetUsesGlobal(overrides => overrides.WorktreesRoot = value ? null : Global.WorktreesRoot ?? string.Empty);
    }

    public bool IsWorktreesRootOverridden => IsRepositoryScope && !WorktreesRootUsesGlobal;
    public bool CanEditWorktreesRoot => !IsRepositoryScope || !WorktreesRootUsesGlobal;

    /// <summary>Onde a raiz cai de fato, no escopo de um repositório.</summary>
    public string? WorktreesRootPreview => Repository is { } repository
        ? $"Neste repositório: {WorktreeCreator.WorktreesRoot(repository.MainWorktreePath ?? repository.RepositoryPath, Effective.WorktreesRoot)}"
        : null;

    public bool KeepSlashes
    {
        get => Repository is null ? Global.WorktreeFolderKeepsSlashes : Effective.WorktreeFolderKeepsSlashes;
        set => Write(KeepSlashesUsesGlobal, () => Global.WorktreeFolderKeepsSlashes = value, overrides => overrides.WorktreeFolderKeepsSlashes = value);
    }

    public bool KeepSlashesUsesGlobal
    {
        get => Overrides?.WorktreeFolderKeepsSlashes is null;
        set => SetUsesGlobal(overrides => overrides.WorktreeFolderKeepsSlashes = value ? null : Global.WorktreeFolderKeepsSlashes);
    }

    public bool IsKeepSlashesOverridden => IsRepositoryScope && !KeepSlashesUsesGlobal;
    public bool CanEditKeepSlashes => !IsRepositoryScope || !KeepSlashesUsesGlobal;

    // ── Gravação ────────────────────────────────────────────────────────────

    /// <summary>
    /// Grava no global ou no override do repositório do escopo; as abas relêem na hora. Campo em
    /// "usar o global" no escopo de um repositório não é escrito: a tela o trava, e escrever
    /// criaria um override que ninguém pediu.
    /// </summary>
    private void Write(bool usesGlobal, Action global, Action<RepositorySettings> repository)
    {
        if (Repository is { } scoped)
        {
            if (usesGlobal)
            {
                RaiseFields();
                return;
            }

            repository(Global.EnsureOverrides(scoped.RepositoryPath));
        }
        else
        {
            global();
        }

        Commit();
    }

    private void SetUsesGlobal(Action<RepositorySettings> apply)
    {
        if (Repository is not { } scoped) return;

        apply(Global.EnsureOverrides(scoped.RepositoryPath));
        Global.PruneOverrides(scoped.RepositoryPath);

        Commit();
    }

    private void Commit()
    {
        _main.SaveSettings();
        _main.ApplySettingsChanged();
        RaiseFields();
    }

    private void RaiseFields()
    {
        foreach (var property in FieldProperties) RaisePropertyChanged(property);
    }

    // ── Escopos ─────────────────────────────────────────────────────────────

    private void OnRepositoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildScopes(Repository);

    /// <summary>Um item para o global e um por aba aberta. Se o repositório do escopo fechou, volta ao global.</summary>
    private void RebuildScopes(RepositoryViewModel? keep)
    {
        var global = Scopes.FirstOrDefault(scope => scope.Repository is null) ?? _selectedScope;

        Scopes.Clear();
        Scopes.Add(global);
        foreach (var repository in _main.Repositories)
            Scopes.Add(new SettingsScope(repository.DisplayName, repository));

        var target = Scopes.FirstOrDefault(scope => keep is not null && ReferenceEquals(scope.Repository, keep)) ?? global;

        _selectedScope = target;
        RaisePropertyChanged(nameof(SelectedScope));
        RaiseFields();
    }
}
