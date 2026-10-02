using System.Text.Json;

namespace Hypercode.Services;

/// <summary>
/// Lê as issues abertas de um repositório para o grafo (#144): uma consulta GraphQL por página
/// de 50, com tipo, milestone, labels e as duas pontas da dependência — nada de uma chamada REST
/// por issue. Custa ~2 pontos da cota GraphQL por página.
/// </summary>
public static class IssueGraphService
{
    public const int PageSize = 50;

    /// <summary>Teto de páginas por leitura: 1000 issues abertas. Passou disso, a leitura avisa que cortou.</summary>
    public const int MaxPages = 20;

    /// <summary>
    /// 50 por ponta da dependência: muito acima do que uma issue costuma ter. Labels, 20. Os
    /// tipos vêm do repositório — null em conta pessoal, que não tem tipos.
    /// </summary>
    internal const string Query = """
        query($owner: String!, $name: String!, $after: String) {
          repository(owner: $owner, name: $name) {
            nameWithOwner
            issueTypes(first: 25) { nodes { name } }
            issues(first: 50, after: $after, states: OPEN, orderBy: {field: CREATED_AT, direction: ASC}) {
              pageInfo { hasNextPage endCursor }
              nodes {
                number title url
                issueType { name color }
                milestone { title }
                labels(first: 20) { nodes { name color } }
                blockedBy(first: 50) { nodes { number title url state repository { nameWithOwner } } }
                blocking(first: 50) { nodes { number title url state repository { nameWithOwner } } }
              }
            }
          }
        }
        """;

    /// <summary>
    /// Todas as páginas. {owner} e {repo} o gh resolve pelo remoto do diretório, como na consulta
    /// dos PRs. Lança com a mensagem do gh (ou do GraphQL) quando falha.
    /// </summary>
    public static async Task<IssueGraphData> LoadAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gh = ExecutableLocator.Find("gh")
            ?? throw new InvalidOperationException("GitHub CLI (gh) não encontrado. Instale com: brew install gh");

        var issues = new List<IssueData>();
        IReadOnlyList<string> types = Array.Empty<string>();
        string? repository = null;
        string? cursor = null;
        var truncated = false;

        for (var page = 0; ; page++)
        {
            if (page == MaxPages)
            {
                truncated = true;
                break;
            }

            var arguments = new List<string> { "api", "graphql", "-F", "owner={owner}", "-F", "name={repo}", "-f", $"query={Query}" };
            if (cursor is not null) arguments.AddRange(new[] { "-f", $"after={cursor}" });

            ProcessResult result;
            try
            {
                result = await ProcessRunner.RunAsync(gh, arguments, repositoryPath, TimeSpan.FromSeconds(60), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("O GitHub não respondeu a tempo.");
            }

            // O gh sai com erro também quando o GraphQL responde "errors": a mensagem vem no stdout.
            var parsed = ParsePage(result.StandardOutput);
            if (parsed is null || !result.Success)
                throw new InvalidOperationException(GraphQLError(result.StandardOutput) ?? result.FirstErrorLine);

            repository ??= parsed.Repository;
            if (page == 0) types = parsed.Types;
            issues.AddRange(parsed.Issues);

            if (parsed.EndCursor is null) break;
            cursor = parsed.EndCursor;
        }

        return new IssueGraphData(repository ?? string.Empty, issues, types, DateTimeOffset.UtcNow, truncated);
    }

    internal sealed record Page(string Repository, IReadOnlyList<IssueData> Issues, IReadOnlyList<string> Types, string? EndCursor);

    /// <summary>Uma página da resposta. Null se ela não tem o repositório — erro, ou JSON que não é este.</summary>
    internal static Page? ParsePage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("repository", out var repository)
                || repository.ValueKind != JsonValueKind.Object
                || !repository.TryGetProperty("issues", out var connection)
                || connection.ValueKind != JsonValueKind.Object)
                return null;

            var name = String(repository, "nameWithOwner") ?? string.Empty;

            var types = Nodes(repository, "issueTypes")
                .Select(node => String(node, "name"))
                .OfType<string>()
                .ToList();

            var issues = Nodes(repository, "issues")
                .Select(node => ParseIssue(node, name))
                .OfType<IssueData>()
                .ToList();

            string? cursor = null;
            if (connection.TryGetProperty("pageInfo", out var pageInfo)
                && pageInfo.TryGetProperty("hasNextPage", out var hasNext)
                && hasNext.ValueKind == JsonValueKind.True)
                cursor = String(pageInfo, "endCursor");

            return new Page(name, issues, types, cursor);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IssueData? ParseIssue(JsonElement node, string repository)
    {
        if (node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("number", out var number)
            || !number.TryGetInt32(out var value))
            return null;

        string? type = null;
        string? typeColor = null;
        if (node.TryGetProperty("issueType", out var issueType) && issueType.ValueKind == JsonValueKind.Object)
        {
            type = String(issueType, "name");
            typeColor = TypeColor(String(issueType, "color"));
        }

        string? milestone = null;
        if (node.TryGetProperty("milestone", out var milestoneNode) && milestoneNode.ValueKind == JsonValueKind.Object)
            milestone = String(milestoneNode, "title");

        var labels = Nodes(node, "labels")
            .Select(label => String(label, "name") is { } name ? new IssueLabel(name, HexColor(String(label, "color"))) : null)
            .OfType<IssueLabel>()
            .ToList();

        return new IssueData(
            repository,
            value,
            String(node, "title") ?? string.Empty,
            String(node, "url") ?? string.Empty,
            type,
            typeColor,
            milestone,
            labels,
            References(node, "blockedBy", repository),
            References(node, "blocking", repository));
    }

    private static IReadOnlyList<IssueRef> References(JsonElement node, string property, string repository)
        => Nodes(node, property)
            .Select(reference =>
            {
                if (!reference.TryGetProperty("number", out var number) || !number.TryGetInt32(out var value)) return null;

                var owner = reference.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object
                    ? String(repo, "nameWithOwner")
                    : null;
                return new IssueRef(
                    owner ?? repository,
                    value,
                    String(reference, "title") ?? string.Empty,
                    String(reference, "url") ?? string.Empty,
                    !string.Equals(String(reference, "state"), "CLOSED", StringComparison.OrdinalIgnoreCase));
            })
            .OfType<IssueRef>()
            .ToList();

    private static IEnumerable<JsonElement> Nodes(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var connection)
           && connection.ValueKind == JsonValueKind.Object
           && connection.TryGetProperty("nodes", out var nodes)
           && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().Where(node => node.ValueKind == JsonValueKind.Object).ToList()
            : Enumerable.Empty<JsonElement>();

    private static string? String(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A primeira mensagem de "errors" de uma resposta GraphQL, se houver.</summary>
    internal static string? GraphQLError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("errors", out var errors)
                   && errors.ValueKind == JsonValueKind.Array
                   && errors.GetArrayLength() > 0
                   && errors[0].TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A cor da label vem em hex sem o #.</summary>
    internal static string? HexColor(string? color)
        => color is { Length: 6 } && color.All(Uri.IsHexDigit) ? "#" + color.ToLowerInvariant() : null;

    /// <summary>
    /// A cor do tipo vem como nome (IssueTypeColor). Os valores são os da paleta do GitHub
    /// para os tipos — é a cor que a pessoa vê no próprio GitHub.
    /// </summary>
    internal static string? TypeColor(string? color) => color?.ToUpperInvariant() switch
    {
        "GRAY" => "#59636e",
        "BLUE" => "#0969da",
        "GREEN" => "#1a7f37",
        "YELLOW" => "#9a6700",
        "ORANGE" => "#bc4c00",
        "RED" => "#d1242f",
        "PINK" => "#bf3989",
        "PURPLE" => "#8250df",
        _ => null,
    };
}
