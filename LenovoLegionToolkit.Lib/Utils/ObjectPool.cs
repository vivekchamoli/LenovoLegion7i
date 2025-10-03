using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Phase 4: Generic object pooling for memory optimization
/// Reduces GC pressure by reusing frequently allocated objects
/// </summary>
public class ObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _pool = new();
    private readonly Func<T> _objectFactory;
    private readonly Action<T>? _resetAction;
    private readonly int _maxPoolSize;
    private int _currentSize;

    public ObjectPool(Func<T> objectFactory, Action<T>? resetAction = null, int maxPoolSize = 100)
    {
        _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
        _resetAction = resetAction;
        _maxPoolSize = maxPoolSize;
        _currentSize = 0;
    }

    /// <summary>
    /// Gets an object from the pool or creates a new one
    /// </summary>
    public T Rent()
    {
        if (!FeatureFlags.UseObjectPooling)
            return _objectFactory();

        if (_pool.TryTake(out var item))
        {
            Interlocked.Decrement(ref _currentSize);
            return item;
        }

        return _objectFactory();
    }

    /// <summary>
    /// Returns an object to the pool
    /// </summary>
    public void Return(T item)
    {
        if (!FeatureFlags.UseObjectPooling || item == null)
            return;

        // Reset object state if action provided
        _resetAction?.Invoke(item);

        // Only add to pool if under size limit
        if (_currentSize < _maxPoolSize)
        {
            _pool.Add(item);
            Interlocked.Increment(ref _currentSize);
        }
    }

    /// <summary>
    /// Clears the pool
    /// </summary>
    public void Clear()
    {
        while (_pool.TryTake(out _))
        {
            Interlocked.Decrement(ref _currentSize);
        }
    }

    /// <summary>
    /// Gets current pool size
    /// </summary>
    public int Count => _currentSize;
}

/// <summary>
/// Pooled object wrapper with automatic return on disposal
/// </summary>
public readonly struct PooledObject<T> : IDisposable where T : class
{
    private readonly ObjectPool<T> _pool;
    private readonly T _value;

    public PooledObject(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        _value = value;
    }

    public T Value => _value;

    public void Dispose()
    {
        _pool.Return(_value);
    }
}

/// <summary>
/// Extension methods for easier pooling
/// </summary>
public static class ObjectPoolExtensions
{
    public static PooledObject<T> RentDisposable<T>(this ObjectPool<T> pool) where T : class
    {
        var item = pool.Rent();
        return new PooledObject<T>(pool, item);
    }
}

/// <summary>
/// Pre-configured pools for common types
/// </summary>
public static class CommonPools
{
    // RGB state buffer pool
    public static readonly ObjectPool<byte[]> RGBBufferPool = new(
        () => new byte[128],
        buffer => Array.Clear(buffer, 0, buffer.Length),
        maxPoolSize: 50
    );

    // WMI property dictionary pool
    public static readonly ObjectPool<Dictionary<string, object>> PropertyDictionaryPool = new(
        () => new Dictionary<string, object>(capacity: 32),
        dict => dict.Clear(),
        maxPoolSize: 30
    );

    // StringBuilder pool for string operations
    public static readonly ObjectPool<StringBuilder> StringBuilderPool = new(
        () => new StringBuilder(capacity: 256),
        sb => sb.Clear(),
        maxPoolSize: 20
    );
}
