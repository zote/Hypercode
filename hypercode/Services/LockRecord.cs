using System.Globalization;
using System.Text.Json;

namespace Hypercode.Services;

/// <summary>
/// O registro JSON que ferramentas como o supacode gravam no motivo do lock, lido para
/// gente: {"build":"…","createdAt":1787771509,"owner":"supacode","version":"0.10.8"}
/// vira "dono: supacode 0.10.8 (build …)" e "travado: 26/08/2026 16:11 (há 33 dias)".
/// </summary>
public static class LockRecord
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Linhas (rótulo, valor) do registro, na ordem: dono, quando foi travado e os campos
    /// que não conhecemos — esses aparecem como vieram, para não esconder informação.
    /// Devolve null se o motivo não for um objeto JSON; aí quem chama mostra o texto cru.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)>? Describe(
        string? reason,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        if (ParseFields(reason) is not { } fields) return null;

        var lines = new List<(string, string)>();

        if (Take(fields, "owner") is { } owner)
        {
            var tool = Take(fields, "version") is { } version ? $"{owner} {version}" : owner;
            if (Take(fields, "build") is { } build) tool += $" (build {build})";
            lines.Add(("dono", tool));
        }

        if (fields.TryGetValue("createdAt", out var createdAt) && ParseTimestamp(createdAt) is { } created)
        {
            fields.Remove("createdAt");
            lines.Add(("travado", FormatMoment(created, now, zone)));
        }

        foreach (var (key, value) in fields)
            lines.Add((key, LooksLikeMoment(key) && ParseTimestamp(value) is { } moment
                ? FormatMoment(moment, now, zone)
                : Text(value)));

        return lines;
    }

    /// <summary>"26/08/2026 16:11 (há 33 dias)", no fuso local.</summary>
    internal static string FormatMoment(DateTimeOffset moment, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        var date = local.ToString("dd/MM/yyyy HH:mm", PtBr);
        return Relative(now - moment) is { } ago ? $"{date} ({ago})" : date;
    }

    private static string? Relative(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) return null;
        if (elapsed.TotalMinutes < 1) return "agora há pouco";
        if (elapsed.TotalHours < 1) return Ago((int)elapsed.TotalMinutes, "minuto", "minutos");
        if (elapsed.TotalDays < 1) return Ago((int)elapsed.TotalHours, "hora", "horas");
        return Ago((int)elapsed.TotalDays, "dia", "dias");
    }

    private static string Ago(int count, string one, string many) => $"há {count} {(count == 1 ? one : many)}";

    /// <summary>Campos do objeto, na ordem do registro. Null se não for um objeto JSON.</summary>
    private static Dictionary<string, JsonElement>? ParseFields(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var text = WorktreeInfo.Unquote(reason.Trim());
        if (!text.StartsWith('{')) return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            // Sem remoções intercaladas com inserções, o Dictionary enumera na ordem em que
            // os campos entraram — a do registro.
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                fields[property.Name] = property.Value.Clone();
            return fields;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Take(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.Remove(key, out var value)) return null;
        var text = Text(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    /// <summary>Chaves que por convenção guardam um instante: updatedAt, locked_at, lockTime…</summary>
    private static bool LooksLikeMoment(string key)
        => key.EndsWith("At", StringComparison.Ordinal)
            || key.EndsWith("_at", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("time", StringComparison.OrdinalIgnoreCase);

    /// <summary>Timestamp Unix em segundos ou milissegundos, ou data ISO 8601.</summary>
    internal static DateTimeOffset? ParseTimestamp(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out var number):
                return FromUnix(number);
            case JsonValueKind.String:
                var text = value.GetString();
                if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
                    return FromUnix(digits);
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed;
                return null;
            default:
                return null;
        }
    }

    private static DateTimeOffset? FromUnix(long value)
    {
        // 10^11 segundos já passa do ano 5000: acima disso, são milissegundos.
        try
        {
            return value > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
