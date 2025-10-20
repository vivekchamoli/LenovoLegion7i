using System;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Represents an active cooling period for a feature
/// </summary>
public class CoolingPeriod
{
    /// <summary>
    /// Feature key (e.g., "DISPLAY_REFRESH_RATE")
    /// </summary>
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>
    /// Scenario that triggered the cooling period
    /// </summary>
    public UserScenario Scenario { get; set; }

    /// <summary>
    /// Value set by the user
    /// </summary>
    public object? UserValue { get; set; }

    /// <summary>
    /// When the cooling period started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When the cooling period expires
    /// </summary>
    public DateTime ExpiryTime { get; set; }

    /// <summary>
    /// Time remaining in the cooling period
    /// </summary>
    public TimeSpan Remaining => ExpiryTime > DateTime.UtcNow ? ExpiryTime - DateTime.UtcNow : TimeSpan.Zero;

    /// <summary>
    /// Whether the cooling period is still active
    /// </summary>
    public bool IsActive => DateTime.UtcNow < ExpiryTime;
}
