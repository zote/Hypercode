using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypercode.Services;

public sealed class Settings
{
    /// <summary>
    /// Chave antiga, da época de um repositório só. Só é lida para migrar — vira a primeira
    /// aba — e some do arquivo no próximo salvamento.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RepositoryPath { get; set; }

    /// <summary>Os repositórios abertos, na ordem das abas. Cada um é a raiz do worktree principal.</summary>
    public List<string> Repositories { get; set; } = new();

    /// <summary>A aba selecionada ao fechar o app, restaurada ao abrir.</summary>
    public string? SelectedRepository { get; set; }

    /// <summary>
    /// O que cada repositório sobrescreve do global, pela raiz do repositório. Fica guardado
    /// mesmo com a aba fechada: reabrir o repositório recupera o que ele tinha.
    /// </summary>
    public Dictionary<string, RepositorySettings> RepositoryOverrides { get; set; } = new(StringComparer.Ordinal);

    public string Command { get; set; } = EffectiveSettings.DefaultCommand;

    /// <summary>
    /// Coluna da ordenação da lista: name, branch, pr ou path. Desde as abas, cada repositório
    /// guarda a sua; esta é a de quem ainda não escolheu.
    /// </summary>
    public string SortColumn { get; set; } = "name";
    public bool SortDescending { get; set; }

    /// <summary>Marca, no diálogo de criação, o checkbox de abrir o terminal no worktree novo.</summary>
    public bool OpenTerminalAfterCreate { get; set; } = true;

    /// <summary>
    /// Quão atento o monitoramento do remoto fica: off, economical, balanced ou aggressive.
    /// Cada worktree tem a sua cadência, pelo estado do PR; o perfil multiplica todas.
    /// </summary>
    public string? MonitorProfile { get; set; }

    /// <summary>
    /// Chave antiga, do intervalo fixo em minutos. Só é lida para migrar (0 vira off) e some
    /// do arquivo no próximo salvamento.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MonitorIntervalMinutes { get; set; }

    /// <summary>Notificação do macOS quando um PR muda (checks, review, merge, conflito).</summary>
    public bool NotifyPullRequestChanges { get; set; } = true;

    /// <summary>
    /// Ao criar um worktree a partir de uma issue, atribui a issue ao usuário do gh se ele ainda
    /// não estiver entre os assignees. Desligada por padrão: é a única preferência que escreve no GitHub.
    /// </summary>
    public bool AssignIssueOnCreate { get; set; }

    /// <summary>
    /// Remove sozinho os worktrees concluídos, sem o diálogo do Limpar concluídos. Desligada por
    /// padrão: ligar passa por um aviso do que o git apaga junto.
    /// </summary>
    public bool AutoCleanup { get; set; }

    /// <summary>Quantos minutos um worktree precisa estar concluído antes de a limpeza automática removê-lo.</summary>
    public int AutoCleanupGraceMinutes { get; set; } = AutoCleanupTracker.DefaultGraceMinutes;

    /// <summary>
    /// Onde o diálogo de criação sugere os worktrees: absoluta, com ~, ou relativa à raiz do
    /// repositório, com {repo} para o nome dele. Null ou vazia, &lt;repo&gt;.worktrees ao lado do repositório.
    /// </summary>
    public string? WorktreesRoot { get; set; }

    /// <summary>A barra da branch vira subpasta (feat/login) em vez de hífen (feat-login).</summary>
    public bool WorktreeFolderKeepsSlashes { get; set; }

    /// <summary>
    /// Repositórios (owner/repo) cuja fila do GitHub Actions o painel acompanha. É global: os
    /// runners são da organização, e a fila não segue as abas abertas. Cada um custa ~3
    /// chamadas REST por ciclo — por isso a escolha é explícita, não os da org inteira.
    /// </summary>
    public List<string> ActionsRepositories { get; set; } = new();

    /// <summary>O painel da fila do Actions aberto ao lado da lista, ou recolhido.</summary>
    public bool ActionsPanelOpen { get; set; }

    /// <summary>A janela própria da fila do Actions aberta — volta aberta na próxima sessão.</summary>
    public bool ActionsWindowOpen { get; set; }

    /// <summary>
    /// O repositório veio da chave antiga, e não de uma aba: pode ser a pasta de um worktree
    /// qualquer, e precisa ser resolvido para o principal antes de virar aba.
    /// </summary>
    [JsonIgnore]
    public string? LegacyRepository { get; set; }

    /// <summary>O override desse repositório, se existir.</summary>
    public RepositorySettings? OverridesFor(string repository)
        => RepositoryOverrides.TryGetValue(repository, out var overrides) ? overrides : null;

    /// <summary>O override desse repositório, criado vazio se ainda não existir.</summary>
    public RepositorySettings EnsureOverrides(string repository)
    {
        if (!RepositoryOverrides.TryGetValue(repository, out var overrides))
        {
            overrides = new RepositorySettings();
            RepositoryOverrides[repository] = overrides;
        }

        return overrides;
    }

    /// <summary>Tira o override que ficou sem nada: o arquivo não acumula entrada vazia.</summary>
    public void PruneOverrides(string repository)
    {
        if (RepositoryOverrides.TryGetValue(repository, out var overrides) && overrides.IsEmpty)
            RepositoryOverrides.Remove(repository);
    }
}

/// <summary>
/// O que um repositório sobrescreve do global. Campo null segue o global. A ordenação da lista
/// não é override: é do repositório, e o global só vale até ele escolher a sua.
/// </summary>
public sealed class RepositorySettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonitorProfile { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NotifyPullRequestChanges { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AssignIssueOnCreate { get; set; }

    /// <summary>A limpeza automática é sobrescrita junto com a carência dela: as duas ou nenhuma.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoCleanup { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AutoCleanupGraceMinutes { get; set; }

    /// <summary>Vazia (e não null) é o padrão explícito, mesmo com o global configurado.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorktreesRoot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WorktreeFolderKeepsSlashes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SortColumn { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SortDescending { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Command is null && MonitorProfile is null && NotifyPullRequestChanges is null && AssignIssueOnCreate is null
        && AutoCleanup is null && AutoCleanupGraceMinutes is null && WorktreesRoot is null && WorktreeFolderKeepsSlashes is null
        && SortColumn is null && SortDescending is null;
}

/// <summary>
/// As preferências que valem para um repositório: o override dele, campo a campo, e o global
/// onde ele não diz nada. Sem repositório, o global puro.
/// </summary>
public sealed record EffectiveSettings(
    string Command,
    MonitorProfile MonitorProfile,
    bool NotifyPullRequestChanges,
    bool AssignIssueOnCreate,
    bool AutoCleanup,
    int AutoCleanupGraceMinutes,
    string? WorktreesRoot,
    bool WorktreeFolderKeepsSlashes)
{
    public const string DefaultCommand = "claude";

    public static EffectiveSettings Resolve(Settings settings, string? repository)
    {
        var overrides = repository is null ? null : settings.OverridesFor(repository);

        return new EffectiveSettings(
            NormalizeCommand(overrides?.Command ?? settings.Command),
            ParseMonitorProfile(overrides?.MonitorProfile ?? settings.MonitorProfile),
            overrides?.NotifyPullRequestChanges ?? settings.NotifyPullRequestChanges,
            overrides?.AssignIssueOnCreate ?? settings.AssignIssueOnCreate,
            overrides?.AutoCleanup ?? settings.AutoCleanup,
            NormalizeGrace(overrides?.AutoCleanupGraceMinutes ?? settings.AutoCleanupGraceMinutes),
            NormalizeWorktreesRoot(overrides?.WorktreesRoot ?? settings.WorktreesRoot),
            overrides?.WorktreeFolderKeepsSlashes ?? settings.WorktreeFolderKeepsSlashes);
    }

    public TimeSpan AutoCleanupGrace => TimeSpan.FromMinutes(AutoCleanupGraceMinutes);

    public static string NormalizeCommand(string? command)
        => string.IsNullOrWhiteSpace(command) ? DefaultCommand : command.Trim();

    public static MonitorProfile ParseMonitorProfile(string? value)
        => Enum.TryParse<MonitorProfile>(value, ignoreCase: true, out var profile) && Enum.IsDefined(profile)
            ? profile
            : MonitorProfile.Balanced;

    public static string FormatMonitorProfile(MonitorProfile profile) => profile.ToString().ToLowerInvariant();

    /// <summary>Null é a raiz padrão; o resto vai sem os espaços das pontas.</summary>
    public static string? NormalizeWorktreesRoot(string? root)
        => string.IsNullOrWhiteSpace(root) ? null : root.Trim();

    public static int NormalizeGrace(int minutes)
        => minutes is >= 1 and <= 1440 ? minutes : AutoCleanupTracker.DefaultGraceMinutes;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Lazy<string> DataDirectoryPath = new(ResolveDataDirectory);

    /// <summary>~/Library/Application Support/Hypercode — preferências e o que mais o app guarda.</summary>
    public static string DataDirectory => DataDirectoryPath.Value;

    private static string ResolveDataDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var supportDirectory = Path.Combine(home, "Library", "Application Support");
        var directory = Path.Combine(supportDirectory, "Hypercode");

        // O app já se chamou Hypertree: sem a cópia, quem atualiza perde o repositório, o
        // comando e a memória dos PRs. Copia em vez de mover para a versão antiga seguir de pé.
        var legacyDirectory = Path.Combine(supportDirectory, "Hypertree");
        if (!Directory.Exists(directory) && Directory.Exists(legacyDirectory))
        {
            try
            {
                CopyDirectory(legacyDirectory, directory);
            }
            catch
            {
                // Migração incompleta não pode derrubar o app: o que faltar volta ao padrão.
            }
        }

        return directory;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var subdirectory in Directory.GetDirectories(source))
            CopyDirectory(subdirectory, Path.Combine(destination, Path.GetFileName(subdirectory)));
    }

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    /// <summary>Lê o JSON do settings.json e migra as chaves antigas. Lança se o JSON for inválido.</summary>
    public static Settings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();

        if (settings.MonitorProfile is null && settings.MonitorIntervalMinutes is <= 0)
            settings.MonitorProfile = "off";
        settings.MonitorIntervalMinutes = null;

        // "null" no arquivo desliga o inicializador; o resto do app conta com as coleções.
        settings.Repositories = (settings.Repositories ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        settings.ActionsRepositories = (settings.ActionsRepositories ?? new List<string>())
            .Select(ActionsQueue.NormalizeRepository)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.RepositoryOverrides = new Dictionary<string, RepositorySettings>(
            (settings.RepositoryOverrides ?? new Dictionary<string, RepositorySettings>())
                .Where(item => item.Value is not null),
            StringComparer.Ordinal);

        // Um repositório só, de antes das abas: vira a primeira.
        if (settings.Repositories.Count == 0 && !string.IsNullOrWhiteSpace(settings.RepositoryPath))
        {
            settings.Repositories.Add(settings.RepositoryPath);
            settings.SelectedRepository = settings.RepositoryPath;
            settings.LegacyRepository = settings.RepositoryPath;
        }
        settings.RepositoryPath = null;

        return settings;
    }

    public static void Save(Settings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, Serialize(settings));
        }
        catch
        {
            // Preferências são conveniência: falhar ao salvar não pode derrubar o app.
        }
    }

    public static string Serialize(Settings settings) => JsonSerializer.Serialize(settings, JsonOptions);
}
