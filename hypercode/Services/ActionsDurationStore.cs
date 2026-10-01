using System.Text.Json;

namespace Hypercode.Services;

/// <summary>
/// As durações medianas de cada repositório acompanhado, em actions-durations.json ao lado do
/// settings.json. A duração de um job muda devagar: o painel lê daqui a cada ciclo e só a
/// coleta, de hora em hora, regrava. Arquivo ausente ou corrompido vale como vazio — a fila
/// fica sem estimativa até a próxima coleta.
/// </summary>
public sealed class ActionsDurationStore
{
    /// <summary>Durações mais velhas que isto são coletadas de novo.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public ActionsDurationStore(string filePath) => _filePath = filePath;

    public static ActionsDurationStore Default { get; } = new(Path.Combine(SettingsStore.DataDirectory, "actions-durations.json"));

    public Dictionary<string, RepositoryDurations> Load()
    {
        try
        {
            if (File.Exists(_filePath)
                && JsonSerializer.Deserialize<Dictionary<string, RepositoryDurations>>(File.ReadAllText(_filePath), JsonOptions) is { } all)
            {
                return all
                    .Where(item => item.Value is not null)
                    .ToDictionary(
                        item => item.Key,
                        item => new RepositoryDurations
                        {
                            CollectedAt = item.Value.CollectedAt,
                            Jobs = (item.Value.Jobs ?? new()).Where(job => job is not null).ToList(),
                        },
                        StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Corrompido: vale como vazio, e a próxima coleta o substitui.
        }

        return new Dictionary<string, RepositoryDurations>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IReadOnlyDictionary<string, RepositoryDurations> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch
        {
            // Como as preferências: falhar ao gravar não derruba o app, só repete a coleta depois.
        }
    }
}
