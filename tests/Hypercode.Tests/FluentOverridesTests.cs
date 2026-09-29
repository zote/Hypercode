using System.Xml.Linq;
using Xunit;

namespace Hypercode.Tests;

/// <summary>
/// Confere Styles/FluentOverrides.axaml lendo o XAML direto: Light e Dark têm exatamente os
/// mesmos aliases (a lista se repete por limitação do StaticResource em dicionário de tema) e
/// todo alias aponta para um token que existe em Styles/Tokens.axaml — um nome errado só
/// estouraria em runtime, quando o controle pedisse a chave.
/// </summary>
public class FluentOverridesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightEDarkTemOsMesmosAliases()
    {
        var variantes = AliasesPorVariante();

        Assert.Equal(variantes["Light"], variantes["Dark"]);
    }

    [Fact]
    public void TodoAliasApontaParaUmTokenQueExiste()
    {
        var tokens = ChavesDosTokens();
        var aliases = XDocument.Load(Caminho("FluentOverrides.axaml")).Descendants()
            .Where(e => e.Name.LocalName == "StaticResource")
            .Select(e => e.Attribute("ResourceKey")!.Value)
            .Distinct();

        Assert.All(aliases, alias => Assert.Contains(alias, tokens));
    }

    private static Dictionary<string, SortedDictionary<string, string>> AliasesPorVariante()
        => XDocument.Load(Caminho("FluentOverrides.axaml")).Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null)
            .ToDictionary(
                d => d.Attribute(X + "Key")!.Value,
                d => new SortedDictionary<string, string>(d.Elements()
                    .Where(e => e.Name.LocalName == "StaticResource")
                    .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Attribute("ResourceKey")!.Value)));

    /// <summary>Chaves definidas nos tokens, na raiz ou em qualquer variante.</summary>
    private static HashSet<string> ChavesDosTokens()
        => XDocument.Load(Caminho("Tokens.axaml")).Descendants()
            .Select(e => e.Attribute(X + "Key")?.Value)
            .OfType<string>()
            .ToHashSet();

    private static string Caminho(string arquivo) => Path.Combine(AppContext.BaseDirectory, arquivo);
}
