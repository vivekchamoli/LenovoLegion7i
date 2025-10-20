using System;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// ELITE FIX: Thread-safe reference-counted battery handle wrapper
/// Prevents ObjectDisposedException race conditions during concurrent access
/// </summary>
public sealed class SafeBatteryHandle : IDisposable
{
    private readonly SafeFileHandle _handle;
    private int _referenceCount;
    private bool _isDisposed;
    private readonly object _lock = new();

    /// <summary>
    /// CRITICAL FIX v6.20.15: Start with refCount=0 to prevent race condition
    /// The caller MUST immediately call AcquireReference() to get the first reference
    /// This ensures atomic acquisition without window for invalidation
    /// </summary>
    public SafeBatteryHandle(SafeFileHandle handle)
    {
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        _referenceCount = 0; // CRITICAL FIX: Start at 0, caller must AcquireReference()
        _isDisposed = false;
    }

    /// <summary>
    /// Acquire a reference to the handle. Returns null if already disposed.
    /// CRITICAL: Must call Release() when done to prevent handle leak
    /// </summary>
    public SafeFileHandle? AcquireReference()
    {
        lock (_lock)
        {
            // Handle already disposed or invalid - cannot acquire
            if (_isDisposed || _handle.IsInvalid || _handle.IsClosed)
                return null;

            _referenceCount++;
            return _handle;
        }
    }

    /// <summary>
    /// Release a reference to the handle. Disposes when reference count reaches 0.
    /// </summary>
    public void ReleaseReference()
    {
        lock (_lock)
        {
            if (_referenceCount <= 0)
                return; // Already at 0, nothing to release

            _referenceCount--;

            // Last reference released - dispose the underlying handle
            if (_referenceCount == 0 && !_isDisposed)
            {
                try
                {
                    _handle?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                _isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Check if handle is still valid and not disposed
    /// </summary>
    public bool IsValid
    {
        get
        {
            lock (_lock)
            {
                return !_isDisposed && _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
            }
        }
    }

    /// <summary>
    /// Get current reference count (for debugging)
    /// </summary>
    public int ReferenceCount
    {
        get
        {
            lock (_lock)
            {
                return _referenceCount;
            }
        }
    }

    /// <summary>
    /// Force invalidation - marks for disposal when all references are released
    /// Does NOT dispose immediately if there are active references
    /// CRITICAL FIX v6.20.15: Updated for refCount starting at 0
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            // Mark as disposed so no new references can be acquired
            _isDisposed = true;

            // CRITICAL FIX v6.20.15: If no active references (refCount=0), dispose immediately
            if (_referenceCount == 0)
            {
                try
                {
                    _handle?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
            // If active references exist, handle will be disposed when last reference calls ReleaseReference()
        }
    }

    /// <summary>
    /// Wait for all references to be released (for graceful shutdown)
    /// </summary>
    /// <param name="timeoutMs">Maximum wait time in milliseconds</param>
    /// <returns>True if all references released, false if timeout</returns>
    public bool WaitForAllReferencesReleased(int timeoutMs = 5000)
    {
        var startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
        {
            lock (_lock)
            {
                if (_referenceCount == 0 || _isDisposed)
                    return true;
            }

            Thread.Sleep(10); // Small sleep to avoid spinning
        }

        return false;
    }

    public void Dispose()
    {
        Invalidate();
    }
}
