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
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Hypertree", "settings.json");
        }
    }

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
