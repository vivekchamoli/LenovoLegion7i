using System;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Feature flag system for gradual rollout and A/B testing
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Enable WMI query caching (Phase 1 optimization)
    /// </summary>
    public static bool UseWMICache => GetFlag("WMICache", defaultValue: true);

    /// <summary>
    /// Enable reactive sensor streams instead of polling (Phase 2 optimization)
    /// </summary>
    public static bool UseReactiveSensors => GetFlag("ReactiveSensors", defaultValue: false);

    /// <summary>
    /// Enable ML-based AI controller for power mode prediction (Phase 3 optimization)
    /// </summary>
    public static bool UseMLAIController => GetFlag("MLAIController", defaultValue: false);

    /// <summary>
    /// Enable adaptive fan curves based on thermal history (Phase 3 optimization)
    /// </summary>
    public static bool UseAdaptiveFanCurves => GetFlag("AdaptiveFanCurves", defaultValue: false);

    /// <summary>
    /// Enable GPU rendering optimization (auto-switch based on power state)
    /// </summary>
    public static bool UseGPURendering => GetFlag("GPURendering", defaultValue: true);

    /// <summary>
    /// Enable performance telemetry and diagnostics
    /// </summary>
    public static bool EnableTelemetry => GetFlag("Telemetry", defaultValue: true);

    /// <summary>
    /// Enable object pooling for RGB operations
    /// </summary>
    public static bool UseObjectPooling => GetFlag("ObjectPooling", defaultValue: false);

    /// <summary>
    /// Get feature flag value from environment variable or default
    /// </summary>
    /// <param name="name">Feature flag name</param>
    /// <param name="defaultValue">Default value if not set</param>
    /// <returns>Feature flag value</returns>
    private static bool GetFlag(string name, bool defaultValue)
    {
        var envVar = Environment.GetEnvironmentVariable($"LLT_FEATURE_{name.ToUpperInvariant()}");

        if (string.IsNullOrEmpty(envVar))
            return defaultValue;

        return bool.TryParse(envVar, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Get all feature flags and their current values (for diagnostics)
    /// </summary>
    public static string GetAllFlags()
    {
        return $"""
            Feature Flags Status:
            - WMI Cache: {UseWMICache}
            - Reactive Sensors: {UseReactiveSensors}
            - ML AI Controller: {UseMLAIController}
            - Adaptive Fan Curves: {UseAdaptiveFanCurves}
            - GPU Rendering: {UseGPURendering}
            - Telemetry: {EnableTelemetry}
            - Object Pooling: {UseObjectPooling}

            Set via environment variables:
            LLT_FEATURE_WMICACHE=true/false
            LLT_FEATURE_REACTIVESENSORS=true/false
            etc.
            """;
    }
}
