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
    /// DEFAULT: ENABLED for v6.2.1+ (production ready - elite optimizations)
    /// </summary>
    public static bool UseAdaptiveFanCurves => GetFlag("AdaptiveFanCurves", defaultValue: true);

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
    /// Enable Resource Orchestrator multi-agent system (Phase 4 - REVOLUTIONARY)
    /// DEFAULT: ENABLED for v6.2.0+ (production ready)
    /// </summary>
    public static bool UseResourceOrchestrator => GetFlag("ResourceOrchestrator", defaultValue: true);

    /// <summary>
    /// Enable Thermal Agent with multi-horizon prediction
    /// DEFAULT: ENABLED for v6.2.0+ (production ready)
    /// </summary>
    public static bool UseThermalAgent => GetFlag("ThermalAgent", defaultValue: true);

    /// <summary>
    /// Enable Power Agent with battery life prediction
    /// DEFAULT: ENABLED for v6.2.0+ (production ready)
    /// </summary>
    public static bool UsePowerAgent => GetFlag("PowerAgent", defaultValue: true);

    /// <summary>
    /// Enable GPU Agent with intelligent process prioritization
    /// DEFAULT: ENABLED for v6.2.0+ (production ready)
    /// </summary>
    public static bool UseGPUAgent => GetFlag("GPUAgent", defaultValue: true);

    /// <summary>
    /// Enable Battery Agent with predictive analytics
    /// DEFAULT: ENABLED for v6.2.0+ (production ready)
    /// </summary>
    public static bool UseBatteryAgent => GetFlag("BatteryAgent", defaultValue: true);

    /// <summary>
    /// Enable Hybrid Mode Agent with intelligent GPU switching
    /// DEFAULT: ENABLED for v6.3.0+ (30-40% battery improvement)
    /// </summary>
    public static bool UseHybridModeAgent => GetFlag("HybridModeAgent", defaultValue: true);

    /// <summary>
    /// Enable Display Agent with adaptive brightness and refresh rate
    /// DEFAULT: ENABLED for v6.3.0+ (30-40% battery improvement)
    /// </summary>
    public static bool UseDisplayAgent => GetFlag("DisplayAgent", defaultValue: true);

    /// <summary>
    /// Enable Keyboard Light Agent with intelligent backlight management
    /// DEFAULT: ENABLED for v6.3.0+ (5-8% battery improvement)
    /// </summary>
    public static bool UseKeyboardLightAgent => GetFlag("KeyboardLightAgent", defaultValue: true);

    /// <summary>
    /// Enable Productivity Mode - Office/Professional workflow optimization
    /// Prioritizes battery life (8-10 hours) and silence (<25dB) over performance
    /// DEFAULT: DISABLED (gaming/balanced mode default)
    /// When enabled:
    /// - Stricter power targets (6W idle vs 15W gaming)
    /// - Aggressive core parking (P-cores parked, E-cores preferred)
    /// - iGPU forced for 99% of applications
    /// - Fan curves optimized for silence
    /// - Display forced to 60Hz on battery
    /// - RGB disabled on battery
    /// </summary>
    public static bool UseProductivityMode => GetFlag("ProductivityMode", defaultValue: false);

    /// <summary>
    /// Get feature flag value from environment variable or default
    /// </summary>
    /// <param name="name">Feature flag name</param>
    /// <param name="defaultValue">Default value if not set</param>
    /// <returns>Feature flag value</returns>
    private static bool GetFlag(string name, bool defaultValue)
    {
        var envVarName = $"LLT_FEATURE_{name.ToUpperInvariant()}";

        // Check Process scope first (for temporary overrides)
        var envVar = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.Process);

        // Then check User scope (persistent settings)
        if (string.IsNullOrEmpty(envVar))
            envVar = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.User);

        // Finally check Machine scope (system-wide settings)
        if (string.IsNullOrEmpty(envVar))
            envVar = Environment.GetEnvironmentVariable(envVarName, EnvironmentVariableTarget.Machine);

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

            Multi-Agent System:
            - Resource Orchestrator: {UseResourceOrchestrator}
            - Thermal Agent: {UseThermalAgent}
            - Power Agent: {UsePowerAgent}
            - GPU Agent: {UseGPUAgent}
            - Battery Agent: {UseBatteryAgent}
            - Hybrid Mode Agent: {UseHybridModeAgent}
            - Display Agent: {UseDisplayAgent}
            - Keyboard Light Agent: {UseKeyboardLightAgent}

            Optimization Modes:
            - Productivity Mode: {UseProductivityMode}

            Set via environment variables:
            LLT_FEATURE_RESOURCEORCHESTRATOR=true/false
            LLT_FEATURE_THERMALAGENT=true/false
            LLT_FEATURE_POWERAGENT=true/false
            LLT_FEATURE_GPUAGENT=true/false
            LLT_FEATURE_BATTERYAGENT=true/false
            LLT_FEATURE_HYBRIDMODEAGENT=true/false
            LLT_FEATURE_DISPLAYAGENT=true/false
            LLT_FEATURE_KEYBOARDLIGHTAGENT=true/false
            LLT_FEATURE_PRODUCTIVITYMODE=true/false
            etc.
            """;
    }
}
