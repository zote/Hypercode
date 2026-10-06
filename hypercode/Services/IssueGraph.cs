namespace Hypercode.Services;

/// <summary>
/// Uma issue pelo endereço: owner/repo e número. O repositório vai sem caixa — o mesmo
/// repositório chega escrito de um jeito na consulta e de outro na ponta de uma dependência.
/// </summary>
public readonly record struct IssueKey
{
    public IssueKey(string repository, int number)
    {
        Repository = repository.ToLowerInvariant();
        Number = number;
    }

    public string Repository { get; }

    public int Number { get; }

    public override string ToString() => $"{Repository}#{Number}";
}

public sealed record IssueLabel(string Name, string? Color);

/// <summary>A outra ponta de uma dependência: só o que o GraphQL traz dela, sem as dependências dela.</summary>
public sealed record IssueRef(string Repository, int Number, string Title, string Url, bool IsOpen)
{
    public IssueKey Key => new(Repository, Number);
}

/// <summary>Uma issue aberta do repositório, com o que filtra e as duas pontas da dependência.</summary>
public sealed record IssueData(
    string Repository,
    int Number,
    string Title,
    string Url,
    string? Type,
    string? TypeColor,
    string? Milestone,
    IReadOnlyList<IssueLabel> Labels,
    IReadOnlyList<IssueRef> BlockedBy,
    IReadOnlyList<IssueRef> Blocking)
{
    public IssueKey Key => new(Repository, Number);
}

/// <summary>
/// A leitura de um repositório. <paramref name="Types"/> são os tipos de issue configurados na
/// organização; vazio em conta pessoal, que não tem tipos — e aí o filtro de tipo some.
/// <paramref name="Truncated"/>: o repositório tem mais issues abertas do que a leitura traz.
/// </summary>
public sealed record IssueGraphData(
    string Repository,
    IReadOnlyList<IssueData> Issues,
    IReadOnlyList<string> Types,
    DateTimeOffset ReadAt,
    bool Truncated = false);

/// <summary>
/// Um nó do grafo: uma issue aberta do repositório (com <see cref="Data"/>) ou a ponta de uma
/// dependência que a leitura não traz por inteiro — fechada, ou de outro repositório.
/// </summary>
public sealed record IssueGraphNode(IssueKey Key, string Repository, int Number, string Title, string Url, bool IsOpen, bool IsExternal, IssueData? Data)
{
    /// <summary>Só as abertas deste repositório entram na lista e nos filtros.</summary>
    public bool IsLocalOpen => Data is not null;
}

/// <summary>
/// O grafo de bloqueio (#144): aresta de quem bloqueia para quem é bloqueado. Montado a partir
/// das issues abertas do repositório; as pontas que não estão nelas (fechadas, de outro
/// repositório) entram como nós sem dependências próprias. Sem I/O.
/// </summary>
public sealed class IssueGraph
{
    private readonly Dictionary<IssueKey, IssueGraphNode> _nodes;
    private readonly Dictionary<IssueKey, HashSet<IssueKey>> _successors;
    private readonly Dictionary<IssueKey, HashSet<IssueKey>> _predecessors;

    private IssueGraph(
        Dictionary<IssueKey, IssueGraphNode> nodes,
        Dictionary<IssueKey, HashSet<IssueKey>> successors,
        Dictionary<IssueKey, HashSet<IssueKey>> predecessors)
    {
        _nodes = nodes;
        _successors = successors;
        _predecessors = predecessors;
    }

    public IReadOnlyDictionary<IssueKey, IssueGraphNode> Nodes => _nodes;

    public int EdgeCount => _successors.Values.Sum(set => set.Count);

    public bool HasEdges => EdgeCount > 0;

    /// <summary>As arestas, de quem bloqueia para quem é bloqueado.</summary>
    public IEnumerable<(IssueKey From, IssueKey To)> Edges
        => _successors.SelectMany(item => item.Value.Select(to => (item.Key, to)));

    /// <summary>Quem esta issue bloqueia, diretamente.</summary>
    public IReadOnlyCollection<IssueKey> Successors(IssueKey key)
        => _successors.TryGetValue(key, out var set) ? set : Array.Empty<IssueKey>();

    /// <summary>Quem bloqueia esta issue, diretamente.</summary>
    public IReadOnlyCollection<IssueKey> Predecessors(IssueKey key)
        => _predecessors.TryGetValue(key, out var set) ? set : Array.Empty<IssueKey>();

    public static IssueGraph Build(string repository, IEnumerable<IssueData> issues)
    {
        var nodes = new Dictionary<IssueKey, IssueGraphNode>();
        var successors = new Dictionary<IssueKey, HashSet<IssueKey>>();
        var predecessors = new Dictionary<IssueKey, HashSet<IssueKey>>();
        var local = new IssueKey(repository, 0).Repository;
        var list = issues.ToList();

        foreach (var issue in list)
            nodes[issue.Key] = new IssueGraphNode(issue.Key, issue.Repository, issue.Number, issue.Title, issue.Url, true, false, issue);

        void AddStub(IssueRef reference)
        {
            if (nodes.ContainsKey(reference.Key)) return;
            nodes[reference.Key] = new IssueGraphNode(
                reference.Key, reference.Repository, reference.Number, reference.Title, reference.Url,
                reference.IsOpen, reference.Key.Repository != local, null);
        }

        void AddEdge(IssueKey from, IssueKey to)
        {
            if (from == to) return;
            if (!successors.TryGetValue(from, out var next)) successors[from] = next = new HashSet<IssueKey>();
            if (!predecessors.TryGetValue(to, out var previous)) predecessors[to] = previous = new HashSet<IssueKey>();
            next.Add(to);
            previous.Add(from);
        }

        // As duas pontas vêm das duas issues quando ambas estão abertas aqui: o HashSet não duplica.
        foreach (var issue in list)
        {
            foreach (var blocker in issue.BlockedBy)
            {
                AddStub(blocker);
                AddEdge(blocker.Key, issue.Key);
            }

            foreach (var blocked in issue.Blocking)
            {
                AddStub(blocked);
                AddEdge(issue.Key, blocked.Key);
            }
        }

        return new IssueGraph(nodes, successors, predecessors);
    }

    /// <summary>O grafo só com estes nós e as arestas entre eles.</summary>
    public IssueGraph Subgraph(IEnumerable<IssueKey> keep)
    {
        var kept = keep.Where(_nodes.ContainsKey).ToHashSet();
        var nodes = kept.ToDictionary(key => key, key => _nodes[key]);
        var successors = new Dictionary<IssueKey, HashSet<IssueKey>>();
        var predecessors = new Dictionary<IssueKey, HashSet<IssueKey>>();

        foreach (var (from, to) in Edges)
        {
            if (!kept.Contains(from) || !kept.Contains(to)) continue;
            if (!successors.TryGetValue(from, out var next)) successors[from] = next = new HashSet<IssueKey>();
            if (!predecessors.TryGetValue(to, out var previous)) predecessors[to] = previous = new HashSet<IssueKey>();
            next.Add(to);
            previous.Add(from);
        }

        return new IssueGraph(nodes, successors, predecessors);
    }

    /// <summary>
    /// Quantos bloqueadores ainda abertos a issue tem. Bloqueador fechado já não segura nada — o
    /// GitHub mantém a relação depois de fechar, então o totalCount do blockedBy não serve.
    /// </summary>
    public int OpenBlockers(IssueKey key) => Predecessors(key).Count(blocker => _nodes[blocker].IsOpen);

    /// <summary>Aberta e sem bloqueador aberto: dá para pegar agora.</summary>
    public bool IsFree(IssueKey key) => _nodes.TryGetValue(key, out var node) && node.IsOpen && OpenBlockers(key) == 0;

    /// <summary>
    /// O alcance transitivo em blocking: as issues abertas que, somando todos os níveis, ficam
    /// mais perto de destravar se esta for resolvida. Só anda por nós abertos — uma fechada no
    /// meio do caminho já não segura nada depois dela. Ciclo não trava: cada nó entra uma vez.
    /// </summary>
    public IReadOnlySet<IssueKey> Reach(IssueKey key) => Walk(key, Successors);

    /// <summary>Tudo o que, transitivamente, segura esta issue — os abertos.</summary>
    public IReadOnlySet<IssueKey> Upstream(IssueKey key) => Walk(key, Predecessors);

    private HashSet<IssueKey> Walk(IssueKey start, Func<IssueKey, IReadOnlyCollection<IssueKey>> next)
    {
        var seen = new HashSet<IssueKey>();
        var stack = new Stack<IssueKey>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            foreach (var neighbor in next(stack.Pop()))
            {
                if (neighbor == start || !_nodes[neighbor].IsOpen || !seen.Add(neighbor)) continue;
                stack.Push(neighbor);
            }
        }

        return seen;
    }

    /// <summary>
    /// Os ciclos entre issues abertas: componentes fortemente conexos com mais de um nó (Tarjan,
    /// iterativo — recursão aqui é exatamente o travamento que a issue pede para evitar). Cada
    /// ciclo vem ordenado pelo número.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IssueKey>> Cycles()
        => StronglyConnected(open: true)
            .Where(component => component.Count > 1)
            .Select(component => (IReadOnlyList<IssueKey>)component.OrderBy(key => key.Repository, StringComparer.Ordinal).ThenBy(key => key.Number).ToList())
            .OrderBy(component => component[0].Number)
            .ToList();

    /// <summary>Tarjan sem recursão. <paramref name="open"/>: só nós abertos e arestas entre eles.</summary>
    internal List<List<IssueKey>> StronglyConnected(bool open)
    {
        var index = new Dictionary<IssueKey, int>();
        var low = new Dictionary<IssueKey, int>();
        var onStack = new HashSet<IssueKey>();
        var stack = new Stack<IssueKey>();
        var components = new List<List<IssueKey>>();
        var counter = 0;

        IEnumerable<IssueKey> Next(IssueKey key) => Successors(key).Where(to => !open || _nodes[to].IsOpen);

        foreach (var root in _nodes.Keys.OrderBy(key => key.Repository, StringComparer.Ordinal).ThenBy(key => key.Number))
        {
            if (index.ContainsKey(root) || (open && !_nodes[root].IsOpen)) continue;

            var work = new Stack<(IssueKey Node, IEnumerator<IssueKey> Edges)>();
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack.Add(root);
            work.Push((root, Next(root).GetEnumerator()));

            while (work.Count > 0)
            {
                var (node, edges) = work.Peek();
                if (edges.MoveNext())
                {
                    var to = edges.Current;
                    if (!index.TryGetValue(to, out var toIndex))
                    {
                        index[to] = low[to] = counter++;
                        stack.Push(to);
                        onStack.Add(to);
                        work.Push((to, Next(to).GetEnumerator()));
                    }
                    else if (onStack.Contains(to))
                    {
                        low[node] = Math.Min(low[node], toIndex);
                    }

                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] != index[node]) continue;

                var component = new List<IssueKey>();
                IssueKey member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                } while (member != node);

                components.Add(component);
            }
        }

        return components;
    }
}

/// <summary>
/// Onde cada nó cai no desenho: a coluna (camada) e a linha dentro dela. As colunas trazem
/// também os pontos de passagem (<see cref="IsWaypoint"/>) das arestas que pulam colunas: a
/// aresta passa por um vão reservado em cada coluna do meio, em vez de cruzar por trás de um
/// cartão e parecer sair dele.
/// </summary>
public sealed record IssueLayout(
    IReadOnlyList<IReadOnlyList<IssueKey>> Layers,
    IReadOnlyDictionary<(IssueKey From, IssueKey To), IReadOnlyList<IssueKey>> Waypoints)
{
    /// <summary>Ponto de passagem: não é issue — repositório vazio, que nenhuma issue tem.</summary>
    public static bool IsWaypoint(IssueKey key) => key.Repository.Length == 0;

    /// <summary>Os pontos de passagem da aresta, da esquerda para a direita; nenhum se ela liga colunas vizinhas.</summary>
    public IReadOnlyList<IssueKey> Route(IssueKey from, IssueKey to)
        => Waypoints.TryGetValue((from, to), out var route) ? route : Array.Empty<IssueKey>();

    public int LayerOf(IssueKey key)
    {
        for (var layer = 0; layer < Layers.Count; layer++)
            if (Layers[layer].Contains(key)) return layer;
        return -1;
    }

    /// <summary>
    /// Grafo em camadas, da esquerda para a direita: a coluna de um nó é quantos níveis de
    /// dependência vêm antes dele (o caminho mais longo desde quem não é bloqueado por ninguém).
    /// Um ciclo é contraído num nó só antes de contar — os membros dividem a coluna, e a conta
    /// termina. A aresta que pula colunas ganha um ponto de passagem em cada uma do meio. Depois,
    /// algumas passadas de baricentro reordenam cada coluna para cruzar menos arestas.
    /// </summary>
    public static IssueLayout Compute(IssueGraph graph, int sweeps = 4)
    {
        // Contração: cada componente fortemente conexo vira um nó.
        var components = graph.StronglyConnected(open: false);
        var componentOf = new Dictionary<IssueKey, int>();
        for (var i = 0; i < components.Count; i++)
            foreach (var key in components[i]) componentOf[key] = i;

        var next = components.Select(_ => new HashSet<int>()).ToArray();
        var incoming = new int[components.Count];
        foreach (var (from, to) in graph.Edges)
        {
            var a = componentOf[from];
            var b = componentOf[to];
            if (a != b && next[a].Add(b)) incoming[b]++;
        }

        // Kahn: caminho mais longo num DAG, na ordem topológica.
        var depth = new int[components.Count];
        var ready = new Queue<int>(Enumerable.Range(0, components.Count).Where(i => incoming[i] == 0));
        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            foreach (var target in next[current])
            {
                depth[target] = Math.Max(depth[target], depth[current] + 1);
                if (--incoming[target] == 0) ready.Enqueue(target);
            }
        }

        var layerCount = components.Count == 0 ? 0 : depth.Max() + 1;
        var layers = Enumerable.Range(0, layerCount).Select(_ => new List<IssueKey>()).ToList();

        // Ordem inicial estável, pelo endereço: a mesma leitura dá sempre o mesmo desenho.
        foreach (var key in componentOf.Keys.OrderBy(key => key.Repository, StringComparer.Ordinal).ThenBy(key => key.Number))
            layers[depth[componentOf[key]]].Add(key);

        // Pontos de passagem: a aresta longa vira uma corrente de segmentos entre colunas vizinhas.
        // A de dentro de um ciclo liga a mesma coluna e é desenhada à parte.
        var successors = new Dictionary<IssueKey, List<IssueKey>>();
        var predecessors = new Dictionary<IssueKey, List<IssueKey>>();
        var waypoints = new Dictionary<(IssueKey From, IssueKey To), IReadOnlyList<IssueKey>>();
        var serial = 0;

        void Link(IssueKey from, IssueKey to)
        {
            if (!successors.TryGetValue(from, out var after)) successors[from] = after = new List<IssueKey>();
            if (!predecessors.TryGetValue(to, out var before)) predecessors[to] = before = new List<IssueKey>();
            after.Add(to);
            before.Add(from);
        }

        foreach (var (from, to) in graph.Edges
                     .OrderBy(edge => edge.From.Repository, StringComparer.Ordinal).ThenBy(edge => edge.From.Number)
                     .ThenBy(edge => edge.To.Repository, StringComparer.Ordinal).ThenBy(edge => edge.To.Number))
        {
            var start = depth[componentOf[from]];
            var end = depth[componentOf[to]];
            if (start == end) continue;

            var route = new List<IssueKey>();
            var previous = from;
            for (var layer = start + 1; layer < end; layer++)
            {
                var waypoint = new IssueKey(string.Empty, ++serial);
                layers[layer].Add(waypoint);
                route.Add(waypoint);
                Link(previous, waypoint);
                previous = waypoint;
            }

            Link(previous, to);
            if (route.Count > 0) waypoints[(from, to)] = route;
        }

        IReadOnlyCollection<IssueKey> After(IssueKey key) => successors.TryGetValue(key, out var list) ? list : Array.Empty<IssueKey>();
        IReadOnlyCollection<IssueKey> Before(IssueKey key) => predecessors.TryGetValue(key, out var list) ? list : Array.Empty<IssueKey>();

        Reorder(After, Before, layers, sweeps);
        return new IssueLayout(layers.Select(layer => (IReadOnlyList<IssueKey>)layer).ToList(), waypoints);
    }

    /// <summary>
    /// Baricentro: cada nó vai para a média da posição dos vizinhos na coluna ao lado — na
    /// descida pelos de quem depende, na subida pelos que ele bloqueia. Nó sem vizinho ali
    /// fica onde estava. Fica a melhor ordem vista, para uma passada ruim não piorar o resultado.
    /// </summary>
    private static void Reorder(
        Func<IssueKey, IReadOnlyCollection<IssueKey>> after,
        Func<IssueKey, IReadOnlyCollection<IssueKey>> before,
        List<List<IssueKey>> layers,
        int sweeps)
    {
        var best = layers.Select(layer => layer.ToList()).ToList();
        var bestCrossings = Crossings(after, layers);

        for (var sweep = 0; sweep < sweeps && bestCrossings > 0; sweep++)
        {
            var down = sweep % 2 == 0;
            var order = down ? Enumerable.Range(1, layers.Count - 1) : Enumerable.Range(0, layers.Count - 1).Reverse();

            foreach (var i in order)
            {
                var neighbor = layers[down ? i - 1 : i + 1];
                var position = neighbor.Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index);

                layers[i] = layers[i]
                    .Select((key, index) =>
                    {
                        var adjacent = (down ? before(key) : after(key))
                            .Where(position.ContainsKey)
                            .Select(other => (double)position[other])
                            .ToList();
                        return (key, center: adjacent.Count == 0 ? index : adjacent.Average(), index);
                    })
                    .OrderBy(item => item.center)
                    .ThenBy(item => item.index)
                    .Select(item => item.key)
                    .ToList();
            }

            var crossings = Crossings(after, layers);
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                best = layers.Select(layer => layer.ToList()).ToList();
            }
        }

        for (var i = 0; i < layers.Count; i++) layers[i] = best[i];
    }

    /// <summary>Cruzamentos entre colunas vizinhas, com as arestas do grafo — sem pontos de passagem.</summary>
    internal static int Crossings(IssueGraph graph, IReadOnlyList<IReadOnlyList<IssueKey>> layers)
        => Crossings(graph.Successors, layers);

    /// <summary>
    /// Cruzamentos entre colunas vizinhas. Com os pontos de passagem, toda aresta fora de ciclo
    /// é uma corrente de segmentos entre vizinhas, e a conta pega todas; as de dentro de um
    /// ciclo ficam de fora — é a medida que o baricentro consegue mexer.
    /// </summary>
    private static int Crossings(Func<IssueKey, IReadOnlyCollection<IssueKey>> after, IReadOnlyList<IReadOnlyList<IssueKey>> layers)
    {
        var crossings = 0;
        for (var layer = 0; layer + 1 < layers.Count; layer++)
        {
            var position = layers[layer + 1].Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index);
            var segments = layers[layer]
                .SelectMany((key, index) => after(key).Where(position.ContainsKey).Select(to => (From: index, To: position[to])))
                .ToList();

            for (var a = 0; a < segments.Count; a++)
                for (var b = a + 1; b < segments.Count; b++)
                    if ((segments[a].From - segments[b].From) * (segments[a].To - segments[b].To) < 0)
                        crossings++;
        }

        return crossings;
    }
}
