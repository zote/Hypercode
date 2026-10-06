using System.Collections.ObjectModel;

namespace Hypercode.ViewModels;

/// <summary>
/// Leva uma coleção da tela ao conteúdo de uma leitura nova mexendo só no que mudou. Limpar e
/// preencher de novo faz o ItemsControl recriar todos os itens, e o tooltip aberto sobre um
/// deles fecha e reabre — pisca a cada ciclo, mesmo sem nada ter mudado (#153).
/// </summary>
internal static class CollectionSync
{
    /// <summary>Posição a posição: o item igual fica (a mesma instância), o diferente é trocado.</summary>
    public static void Apply<T>(IList<T> target, IReadOnlyList<T> source, Func<T, T, bool>? same = null)
    {
        same ??= EqualityComparer<T>.Default.Equals;

        for (var index = 0; index < source.Count; index++)
        {
            if (index == target.Count) target.Add(source[index]);
            else if (!same(target[index], source[index])) target[index] = source[index];
        }

        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    /// <summary>
    /// Para grupos com itens dentro (runners de um grupo, jobs de uma fila). O grupo com o mesmo
    /// cabeçalho fica e só os itens dele são sincronizados; o grupo que entra leva os itens numa
    /// <see cref="ObservableCollection{T}"/>, para a próxima leitura poder mexer neles no lugar.
    /// </summary>
    public static void ApplyGroups<TGroup, TItem>(
        IList<TGroup> target,
        IReadOnlyList<TGroup> source,
        Func<TGroup, IReadOnlyList<TItem>> items,
        Func<TGroup, ObservableCollection<TItem>, TGroup> withItems,
        Func<TGroup, TGroup, bool> sameHeader)
    {
        for (var index = 0; index < source.Count; index++)
        {
            var fresh = source[index];
            if (index < target.Count
                && sameHeader(target[index], fresh)
                && items(target[index]) is ObservableCollection<TItem> shown)
            {
                Apply(shown, items(fresh));
                continue;
            }

            var group = withItems(fresh, new ObservableCollection<TItem>(items(fresh)));
            if (index == target.Count) target.Add(group);
            else target[index] = group;
        }

        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }
}
