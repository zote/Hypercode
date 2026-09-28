using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypercode.Services;

public sealed class Settings
{
    public string? RepositoryPath { get; set; }
    public string Command { get; set; } = "claude";

    /// <summary>Coluna da ordenação da lista: name, branch, pr ou path.</summary>
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
            if (!File.Exists(FilePath)) return new Settings();
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();

            if (settings.MonitorProfile is null && settings.MonitorIntervalMinutes is <= 0)
                settings.MonitorProfile = "off";
            settings.MonitorIntervalMinutes = null;

            return settings;
        }
        catch
        {
            return new Settings();
        }
    }

    public static void Save(Settings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Preferências são conveniência: falhar ao salvar não pode derrubar o app.
        }
    }
}
