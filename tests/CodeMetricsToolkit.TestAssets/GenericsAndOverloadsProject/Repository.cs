namespace GenericsAndOverloadsProject;

public sealed class Repository<T> where T : notnull
{
    private readonly List<T> _items = new();

    public Repository()
    {
    }

    public Repository(IEnumerable<T> initialItems)
    {
        _items.AddRange(initialItems);
    }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public void Add(T item)
    {
        _items.Add(item);
    }

    public void Add(IEnumerable<T> items)
    {
        _items.AddRange(items);
    }

    public T? Find<TKey>(TKey key, Func<T, TKey> keySelector)
        where TKey : notnull
    {
        foreach (T item in _items)
        {
            if (EqualityComparer<TKey>.Default.Equals(keySelector(item), key))
            {
                return item;
            }
        }

        return default;
    }

    public IReadOnlyList<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        return _items.Select(selector).ToArray();
    }
}
