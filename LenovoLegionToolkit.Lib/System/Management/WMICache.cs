using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.Lib.System.Management;

/// <summary>
/// WMI query caching layer for performance optimization
/// Reduces redundant WMI calls by caching results with configurable TTL
/// </summary>
public class WMICache
{
    private readonly ConcurrentDictionary<string, CachedQuery> _cache = new();
    private readonly Timer? _cleanupTimer;

    private class CachedQuery
    {
        public IEnumerable<ManagementBaseObject>? Result { get; init; }
        public DateTime Expiration { get; init; }
    }

    public WMICache()
    {
        // Cleanup expired entries every 60 seconds
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Execute WMI query with caching support
    /// </summary>
    /// <param name="scope">WMI namespace scope</param>
    /// <param name="query">WMI query string</param>
    /// <param name="cacheDuration">Cache duration (default: 5 minutes, TimeSpan.Zero to disable cache)</param>
    /// <returns>WMI query results</returns>
    public async Task<IEnumerable<ManagementBaseObject>> QueryAsync(
        string scope,
        string query,
        TimeSpan cacheDuration = default)
    {
        cacheDuration = cacheDuration == default ? TimeSpan.FromMinutes(5) : cacheDuration;

        // Cache disabled for zero duration
        if (cacheDuration == TimeSpan.Zero)
            return await ExecuteQueryAsync(scope, query).ConfigureAwait(false);

        var cacheKey = $"{scope}::{query}";

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTime.UtcNow < cached.Expiration && cached.Result is not null)
                return cached.Result;

            // Expired, remove from cache
            _cache.TryRemove(cacheKey, out _);
        }

        // Execute query
        var result = await ExecuteQueryAsync(scope, query).ConfigureAwait(false);

        // Cache result
        _cache[cacheKey] = new CachedQuery
        {
            Result = result,
            Expiration = DateTime.UtcNow.Add(cacheDuration)
        };

        return result;
    }

    /// <summary>
    /// Invalidate cache entries matching pattern
    /// </summary>
    /// <param name="pattern">Pattern to match (use "*" for all)</param>
    public void InvalidateCache(string pattern = "*")
    {
        if (pattern == "*")
        {
            _cache.Clear();
            return;
        }

        var keysToRemove = new List<string>();
        foreach (var key in _cache.Keys)
        {
            if (key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                keysToRemove.Add(key);
        }

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Execute WMI query with proper disposal
    /// </summary>
    private static async Task<IEnumerable<ManagementBaseObject>> ExecuteQueryAsync(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        return await searcher.GetAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Cleanup expired cache entries
    /// </summary>
    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _cache)
        {
            if (now >= kvp.Value.Expiration)
                keysToRemove.Add(kvp.Key);
        }

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cache.Clear();
    }
}
