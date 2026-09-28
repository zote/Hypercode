using System.Text.Json;

namespace Hypertree.Services;

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
    /// De quantos em quantos minutos o app faz fetch e relê os PRs no GitHub com a janela
    /// ativa. Em segundo plano o intervalo triplica; minimizado, pausa. 0 desliga.
    /// </summary>
    public int MonitorIntervalMinutes { get; set; } = 5;

    /// <summary>Notificação do macOS quando um PR muda (checks, review, merge, conflito).</summary>
    public bool NotifyPullRequestChanges { get; set; } = true;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>~/Library/Application Support/Hypertree — preferências e o que mais o app guarda.</summary>
    public static string DataDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Hypertree");
        }
    }

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
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
