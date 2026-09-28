namespace Hypertree.Services;

/// <summary>
/// Sessões gravadas pelo Claude Code. Ele guarda cada conversa em
/// <c>~/.claude/projects/&lt;pasta&gt;/&lt;id&gt;.jsonl</c>, onde a pasta é o caminho absoluto
/// com todo caractere que não é letra nem dígito trocado por '-'
/// (<c>/Users/eu/repo.worktrees/x</c> → <c>-Users-eu-repo-worktrees-x</c>).
/// </summary>
public static class ClaudeSessions
{
    /// <summary>Retoma a conversa mais recente da pasta atual.</summary>
    public const string ContinueCommand = "claude --continue";

    public static bool Exist(string directory)
    {
        try
        {
            var folder = Path.Combine(ProjectsRoot(), EncodeProjectPath(directory));
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.jsonl").Any();
        }
        catch (Exception)
        {
            // Sem permissão ou caminho estranho: trata como "sem sessão".
            return false;
        }
    }

    internal static string EncodeProjectPath(string directory)
    {
        var path = directory.TrimEnd('/');
        return string.Create(path.Length, path, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = char.IsAsciiLetterOrDigit(source[i]) ? source[i] : '-';
        });
    }

    // CLAUDE_CONFIG_DIR só chega aqui se o app foi aberto pelo terminal; pelo Finder vale o padrão.
    private static string ProjectsRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configured;
        return Path.Combine(root, "projects");
    }
}
