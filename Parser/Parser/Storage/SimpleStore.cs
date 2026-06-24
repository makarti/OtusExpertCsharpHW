namespace Parser.Storage;

public sealed class SimpleStore : IDisposable
{
    private readonly Dictionary<string, byte[]> _store = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    private long _setCount;
    private long _getCount;
    private long _deleteCount;

    public void Set(string key, byte[] value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        _lock.EnterWriteLock();
        try
        {
            _store[key] = value;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Interlocked.Increment(ref _setCount);
    }

    public byte[]? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        _lock.EnterReadLock();
        byte[]? result;
        try
        {
            _store.TryGetValue(key, out result);
        }
        finally
        {
            _lock.ExitReadLock();
        }

        Interlocked.Increment(ref _getCount);
        return result;
    }

    public void Delete(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        _lock.EnterWriteLock();
        try
        {
            _store.Remove(key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Interlocked.Increment(ref _deleteCount);
    }

    public (long SetCount, long GetCount, long DeleteCount) GetStatistics() =>
    (
        Interlocked.Read(ref _setCount),
        Interlocked.Read(ref _getCount),
        Interlocked.Read(ref _deleteCount)
    );

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _store.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
