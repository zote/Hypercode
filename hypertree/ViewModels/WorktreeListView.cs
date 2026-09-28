namespace Hypertree.ViewModels;

/// <summary>
/// O que a lista mostra: as linhas filtradas pelo texto do filtro e ordenadas pela coluna
/// escolhida. Função pura, separada do <see cref="MainViewModel"/> para ser testável.
/// </summary>
public static class WorktreeListView
{
    /// <summary>
    /// Cada palavra do filtro precisa casar com a linha (<see cref="WorktreeRow.Matches"/>).
    /// O principal fica sempre no topo, seja qual for a ordenação; linhas sem PR vão para o
    /// fim quando a ordenação é por PR, nos dois sentidos.
    /// </summary>
    public static List<WorktreeRow> Arrange(
        IEnumerable<WorktreeRow> rows,
        string filterText,
        SortColumn column,
        bool descending)
    {
        var terms = filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var filtered = rows.Where(row => terms.All(row.Matches));

        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var ordered = filtered.OrderBy(row => row.Worktree.IsMain ? 0 : 1);

        ordered = column switch
        {
            SortColumn.Branch => ordered.Then(row => row.Branch, comparer, descending),
            SortColumn.PullRequest => ordered
                .ThenBy(row => row.PullRequest is null ? 1 : 0)
                .Then(row => row.PullRequest?.Number ?? 0, Comparer<int>.Default, descending),
            SortColumn.Path => ordered.Then(row => row.FullPath, comparer, descending),
            _ => ordered.Then(row => row.Name, comparer, descending),
        };

        return ordered.ToList();
    }

    private static IOrderedEnumerable<T> Then<T, TKey>(
        this IOrderedEnumerable<T> source,
        Func<T, TKey> key,
        IComparer<TKey> comparer,
        bool descending)
        => descending ? source.ThenByDescending(key, comparer) : source.ThenBy(key, comparer);
}
