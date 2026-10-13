namespace MulticlassRace.Common;

/// <summary>Tracks which column a table is sorted by and in which direction.</summary>
public sealed class SortState<TColumn> where TColumn : notnull
{
    public SortState(TColumn column, bool descending = false)
    {
        Column = column;
        Descending = descending;
    }

    public TColumn Column { get; private set; }

    public bool Descending { get; private set; }

    /// <summary>Clicking the active column flips direction; clicking another column starts ascending.</summary>
    public void Toggle(TColumn column)
    {
        if (EqualityComparer<TColumn>.Default.Equals(Column, column))
        {
            Descending = !Descending;
            return;
        }

        Column = column;
        Descending = false;
    }

    public IOrderedEnumerable<T> Apply<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        return Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
    }

    public IOrderedEnumerable<T> Apply<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, IComparer<TKey> comparer)
    {
        return Descending
            ? source.OrderByDescending(keySelector, comparer)
            : source.OrderBy(keySelector, comparer);
    }
}
