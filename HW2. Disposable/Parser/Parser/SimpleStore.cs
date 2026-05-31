namespace Parser;

public sealed class SimpleStore
{
    private readonly Dictionary<string, byte[]> _store = new();

    public void Set(string key, byte[] value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        _store[key] = value;
    }

    public byte[]? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return _store.TryGetValue(key, out var value) ? value : null;
    }

    public void Delete(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        _store.Remove(key);
    }

    public int Count => _store.Count;
}
