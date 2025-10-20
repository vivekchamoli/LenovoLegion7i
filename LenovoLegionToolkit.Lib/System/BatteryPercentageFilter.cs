using System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// ELITE PERFORMANCE REFACTORING: Battery percentage spike filter
/// Extracted from Battery.cs for maintainability and testability
/// Prevents spurious x% → 100% → x% fluctuations from Windows GetSystemPowerStatus
/// </summary>
public class BatteryPercentageFilter
{
    // Filter configuration
    private readonly BatteryPercentageFilterConfig _config;

    // Filter state
    private int _lastValidPercentage = -1; // -1 = not initialized
    private DateTime _lastPercentageUpdateTime = DateTime.MinValue;
    private bool _wasChargingLastUpdate = false;
    private int _last100PercentCount = 0;
    private DateTime _last100PercentTime = DateTime.MinValue;

    private readonly object _lock = new();

    public BatteryPercentageFilter(BatteryPercentageFilterConfig? config = null)
    {
        _config = config ?? BatteryPercentageFilterConfig.Default;
    }

    /// <summary>
    /// Filter battery percentage to remove spikes and transients
    /// Thread-safe for concurrent access
    /// </summary>
    public int Filter(int rawPercentage, bool isCharging)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastUpdate = (now - _lastPercentageUpdateTime).TotalSeconds;

            // First call - initialize with validation
            if (_lastValidPercentage < 0)
            {
                return InitializeFilter(rawPercentage, isCharging, now);
            }

            // Charging state changed - reset filter
            if (isCharging != _wasChargingLastUpdate)
            {
                return ResetFilter(rawPercentage, isCharging, now);
            }

            // Detect and reject spikes
            var percentageDelta = Math.Abs(rawPercentage - _lastValidPercentage);

            // SPIKE DETECTION: Reject unrealistic jumps
            if (rawPercentage == 100 && timeSinceLastUpdate < _config.Quick100SpikeWindowSeconds && percentageDelta > _config.Quick100SpikeThreshold)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Battery percentage SPIKE REJECTED: {_lastValidPercentage}% → 100% in {timeSinceLastUpdate:F1}s (>{_config.Quick100SpikeThreshold}%/{_config.Quick100SpikeWindowSeconds}s - 100% spike)");

                return _lastValidPercentage;
            }

            if (timeSinceLastUpdate < _config.QuickSpikeWindowSeconds && percentageDelta > _config.QuickSpikeThreshold)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Battery percentage SPIKE REJECTED: {_lastValidPercentage}% → {rawPercentage}% in {timeSinceLastUpdate:F1}s (>{_config.QuickSpikeThreshold}%/{_config.QuickSpikeWindowSeconds}s)");

                return _lastValidPercentage;
            }

            if (timeSinceLastUpdate < _config.MediumSpikeWindowSeconds && percentageDelta > _config.MediumSpikeThreshold)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Battery percentage SPIKE REJECTED: {_lastValidPercentage}% → {rawPercentage}% in {timeSinceLastUpdate:F1}s (>{_config.MediumSpikeThreshold}%/{_config.MediumSpikeWindowSeconds}s)");

                return _lastValidPercentage;
            }

            // SPECIAL CASE: Reject spurious 100% spikes when not near full
            if (rawPercentage == 100)
            {
                if (!IsValid100Percent(now))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery percentage SPIKE REJECTED: Spurious 100% (last valid: {_lastValidPercentage}%, charging: {isCharging}, count: {_last100PercentCount})");

                    return _lastValidPercentage;
                }

                if (_last100PercentCount >= _config.Consecutive100RequiredForAcceptance && Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Battery percentage 100% ACCEPTED after {_last100PercentCount} consecutive readings");
                }
            }
            else
            {
                // Not 100% - reset counter
                _last100PercentCount = 0;
            }

            // SPECIAL CASE: Reject spurious 0% drops
            if (rawPercentage == 0 && _lastValidPercentage > _config.MinPercentageForZeroRejection && timeSinceLastUpdate < _config.ZeroDropWindowSeconds)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Battery percentage SPIKE REJECTED: Spurious 0% drop (last valid: {_lastValidPercentage}%, time: {timeSinceLastUpdate:F1}s)");

                return _lastValidPercentage;
            }

            // Valid change - accept and update state
            return AcceptValue(rawPercentage, isCharging, now);
        }
    }

    private int InitializeFilter(int rawPercentage, bool isCharging, DateTime now)
    {
        // CRITICAL FIX: Reject 100% on first call if not charging (ACPI firmware glitch)
        if (rawPercentage == 100 && !isCharging)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery percentage SPIKE REJECTED on FIRST CALL: 100% while discharging (ACPI bug), using {_config.DefaultPercentageOnInvalidInit}% default");

            _lastValidPercentage = _config.DefaultPercentageOnInvalidInit;
            _lastPercentageUpdateTime = now;
            _wasChargingLastUpdate = isCharging;
            return _config.DefaultPercentageOnInvalidInit;
        }

        _lastValidPercentage = rawPercentage;
        _lastPercentageUpdateTime = now;
        _wasChargingLastUpdate = isCharging;
        return rawPercentage;
    }

    private int ResetFilter(int rawPercentage, bool isCharging, DateTime now)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Battery percentage filter: Charging state changed ({_wasChargingLastUpdate} → {isCharging}), resetting filter");

        _lastValidPercentage = rawPercentage;
        _lastPercentageUpdateTime = now;
        _wasChargingLastUpdate = isCharging;
        return rawPercentage;
    }

    private bool IsValid100Percent(DateTime now)
    {
        var timeSinceLast100 = (now - _last100PercentTime).TotalSeconds;

        // If this 100% follows another 100% within the consecutive window, increment counter
        if (timeSinceLast100 < _config.Consecutive100WindowSeconds)
            _last100PercentCount++;
        else
            _last100PercentCount = 1; // Reset counter, this is a new 100% sequence

        _last100PercentTime = now;

        // CRITICAL: Reject 100% unless:
        // 1. Last valid was ≥98% (battery genuinely near full), OR
        // 2. We've seen 3+ consecutive 100% readings (stable full charge)
        return _lastValidPercentage >= _config.MinPercentageFor100Acceptance ||
               _last100PercentCount >= _config.Consecutive100RequiredForAcceptance;
    }

    private int AcceptValue(int rawPercentage, bool isCharging, DateTime now)
    {
        _lastValidPercentage = rawPercentage;
        _lastPercentageUpdateTime = now;
        _wasChargingLastUpdate = isCharging;
        return rawPercentage;
    }

    /// <summary>
    /// Reset filter to initial state
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastValidPercentage = -1;
            _lastPercentageUpdateTime = DateTime.MinValue;
            _wasChargingLastUpdate = false;
            _last100PercentCount = 0;
            _last100PercentTime = DateTime.MinValue;
        }
    }
}

/// <summary>
/// Configuration for battery percentage filter
/// All thresholds tuned based on real-world testing with Legion 7i Gen 9
/// </summary>
public class BatteryPercentageFilterConfig
{
    // Quick spike detection (10 second window)
    public int QuickSpikeWindowSeconds { get; set; } = 10;
    public int QuickSpikeThreshold { get; set; } = 20;

    // Quick 100% spike detection (stricter threshold)
    public int Quick100SpikeWindowSeconds { get; set; } = 10;
    public int Quick100SpikeThreshold { get; set; } = 15;

    // Medium spike detection (30 second window)
    public int MediumSpikeWindowSeconds { get; set; } = 30;
    public int MediumSpikeThreshold { get; set; } = 30;

    // 100% acceptance criteria
    public int MinPercentageFor100Acceptance { get; set; } = 98;
    public int Consecutive100RequiredForAcceptance { get; set; } = 3;
    public int Consecutive100WindowSeconds { get; set; } = 6;

    // 0% rejection criteria
    public int MinPercentageForZeroRejection { get; set; } = 5;
    public int ZeroDropWindowSeconds { get; set; } = 60;

    // Initialization
    public int DefaultPercentageOnInvalidInit { get; set; } = 75;

    public static BatteryPercentageFilterConfig Default => new();
}
