using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Integration service for Resource Orchestrator
/// Handles dependency injection, lifecycle management, and feature flag integration
/// Add this to your Autofac ContainerBuilder registration in App.xaml.cs
/// </summary>
public static class OrchestratorIntegration
{
    /// <summary>
    /// Register all Resource Orchestrator components with Autofac DI container
    /// Call this from App.xaml.cs ConfigureContainer method
    /// </summary>
    /// <example>
    /// var builder = new ContainerBuilder();
    /// // ... existing registrations ...
    /// OrchestratorIntegration.RegisterServices(builder);
    /// </example>
    public static void RegisterServices(ContainerBuilder builder)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Registering Resource Orchestrator services...");

        // Core ML/AI components (already exist, register as singletons)
        builder.RegisterType<PowerUsagePredictor>().SingleInstance();
        builder.RegisterType<ThermalOptimizer>().SingleInstance();

        // New multi-agent system components
        builder.RegisterType<WorkloadClassifier>().SingleInstance();
        builder.RegisterType<SystemContextStore>().SingleInstance();
        builder.RegisterType<DecisionArbitrationEngine>().SingleInstance();
        builder.RegisterType<BatteryLifeEstimator>().SingleInstance();

        // Phase 3: Learning and coordination
        builder.RegisterType<UserBehaviorAnalyzer>().SingleInstance();
        builder.RegisterType<UserPreferenceTracker>().SingleInstance();
        builder.RegisterType<AgentCoordinator>().SingleInstance();

        // Phase 4: Data persistence
        builder.RegisterType<DataPersistenceService>().SingleInstance();

        // Safety and validation
        builder.RegisterType<UserOverrideManager>().SingleInstance();
        builder.RegisterType<SafetyValidator>().SingleInstance();

        // Action execution framework
        builder.RegisterType<CPUPowerLimitHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<GPUControlHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<FanControlHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<PowerModeHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<BatteryControlHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<HybridModeHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<DisplayControlHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<KeyboardBacklightHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<CoordinationHandler>().As<IActionHandler>().SingleInstance();
        builder.RegisterType<ActionExecutor>().SingleInstance();

        // Register optimization agents
        builder.RegisterType<ThermalAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<PowerAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<GPUAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<BatteryAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<HybridModeAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<DisplayAgent>().As<IOptimizationAgent>().SingleInstance();
        builder.RegisterType<KeyboardLightAgent>().As<IOptimizationAgent>().SingleInstance();

        // Register Resource Orchestrator
        builder.RegisterType<ResourceOrchestrator>().SingleInstance();

        // Register lifecycle manager
        builder.RegisterType<OrchestratorLifecycleManager>().SingleInstance();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Resource Orchestrator services registered");
    }

    /// <summary>
    /// Initialize and start the Resource Orchestrator system
    /// Call this from App.xaml.cs after container is built, or from MainWindow
    /// </summary>
    public static async Task InitializeAsync(IContainer container)
    {
        if (!FeatureFlags.UseResourceOrchestrator)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Resource Orchestrator disabled by feature flag");
            return;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Initializing Resource Orchestrator...");

        var lifecycleManager = container.Resolve<OrchestratorLifecycleManager>();
        await lifecycleManager.StartAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Resource Orchestrator initialized and running");
    }

    /// <summary>
    /// Shutdown the Resource Orchestrator system
    /// Call this from App.xaml.cs Exit handler or MainWindow Closing event
    /// </summary>
    public static async Task ShutdownAsync(IContainer container)
    {
        if (!FeatureFlags.UseResourceOrchestrator)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Shutting down Resource Orchestrator...");

        var lifecycleManager = container.Resolve<OrchestratorLifecycleManager>();
        await lifecycleManager.StopAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Resource Orchestrator shutdown complete");
    }
}

/// <summary>
/// Lifecycle manager for Resource Orchestrator
/// Handles startup, agent registration, graceful shutdown, and data persistence
/// </summary>
public class OrchestratorLifecycleManager
{
    private readonly ResourceOrchestrator _orchestrator;
    private readonly IOptimizationAgent[] _agents;
    private readonly DataPersistenceService _persistenceService;
    private readonly UserBehaviorAnalyzer? _behaviorAnalyzer;
    private readonly UserPreferenceTracker? _preferenceTracker;
    private readonly SystemContextStore _contextStore;
    private bool _isStarted;
    private DateTime _firstStart;
    private TimeSpan _totalUptime;

    public OrchestratorLifecycleManager(
        ResourceOrchestrator orchestrator,
        IOptimizationAgent[] agents,
        DataPersistenceService persistenceService,
        SystemContextStore contextStore,
        UserBehaviorAnalyzer? behaviorAnalyzer = null,
        UserPreferenceTracker? preferenceTracker = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _behaviorAnalyzer = behaviorAnalyzer;
        _preferenceTracker = preferenceTracker;
        _firstStart = DateTime.UtcNow;
    }

    /// <summary>
    /// Start the orchestrator with all enabled agents
    /// </summary>
    public async Task StartAsync()
    {
        if (_isStarted)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Already started");
            return;
        }

        if (!FeatureFlags.UseResourceOrchestrator)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Orchestrator disabled by feature flag");
            return;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting Resource Orchestrator lifecycle...");

        // Load persisted data
        await LoadPersistedDataAsync().ConfigureAwait(false);

        // Register agents based on feature flags
        foreach (var agent in _agents)
        {
            var shouldRegister = agent.AgentName switch
            {
                "ThermalAgent" => FeatureFlags.UseThermalAgent,
                "PowerAgent" => FeatureFlags.UsePowerAgent,
                "GPUAgent" => FeatureFlags.UseGPUAgent,
                "BatteryAgent" => FeatureFlags.UseBatteryAgent,
                "HybridModeAgent" => FeatureFlags.UseHybridModeAgent,
                "DisplayAgent" => FeatureFlags.UseDisplayAgent,
                "KeyboardLightAgent" => FeatureFlags.UseKeyboardLightAgent,
                _ => false
            };

            if (shouldRegister)
            {
                _orchestrator.RegisterAgent(agent);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Registered agent: {agent.AgentName}");
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Agent {agent.AgentName} disabled by feature flag");
            }
        }

        // Start optimization loop (500ms = 2Hz)
        await _orchestrator.StartAsync(optimizationIntervalMs: 500).ConfigureAwait(false);

        _isStarted = true;

        // Start auto-save timer (every 5 minutes)
        StartAutoSave();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Resource Orchestrator started with {_agents.Length} agents");
    }

    /// <summary>
    /// Stop the orchestrator gracefully
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isStarted)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping Resource Orchestrator lifecycle...");

        // Save all data before shutdown
        await SavePersistedDataAsync().ConfigureAwait(false);

        await _orchestrator.StopAsync().ConfigureAwait(false);

        _isStarted = false;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Resource Orchestrator lifecycle stopped");
    }

    /// <summary>
    /// Get orchestrator statistics for diagnostics
    /// </summary>
    public OrchestratorStatistics GetStatistics()
    {
        return new OrchestratorStatistics
        {
            IsRunning = _orchestrator.IsRunning,
            TotalCycles = _orchestrator.TotalCycles,
            TotalActions = _orchestrator.TotalActions,
            TotalConflicts = _orchestrator.TotalConflicts,
            UpTime = _orchestrator.UpTime,
            RegisteredAgents = _agents.Length,
            BehaviorDataPoints = _behaviorAnalyzer?.GetDataPointCount() ?? 0,
            LearnedPreferences = _preferenceTracker?.GetLearnedPreferenceCount() ?? 0,
            DataSizeKB = _persistenceService.GetDataSizeBytes() / 1024
        };
    }

    private async Task LoadPersistedDataAsync()
    {
        try
        {
            // Load behavior history
            if (_behaviorAnalyzer != null)
            {
                var behaviorHistory = await _persistenceService.LoadBehaviorHistoryAsync().ConfigureAwait(false);
                if (behaviorHistory.Count > 0)
                    _behaviorAnalyzer.LoadHistory(behaviorHistory);
            }

            // Load user preferences
            if (_preferenceTracker != null)
            {
                var preferences = await _persistenceService.LoadUserPreferencesAsync().ConfigureAwait(false);
                if (preferences != null)
                    _preferenceTracker.ImportData(preferences);
            }

            // Load statistics
            var stats = await _persistenceService.LoadStatisticsAsync().ConfigureAwait(false);
            if (stats != null)
            {
                _firstStart = stats.FirstStart;
                _totalUptime = stats.TotalUptime;
            }

            // Load battery history
            var batteryHistory = await _persistenceService.LoadBatteryHistoryAsync().ConfigureAwait(false);
            // Battery history is already managed by SystemContextStore

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Persisted data loaded successfully");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load persisted data", ex);
        }
    }

    private async Task SavePersistedDataAsync()
    {
        try
        {
            // Save behavior history
            if (_behaviorAnalyzer != null)
            {
                var behaviorHistory = _behaviorAnalyzer.GetHistory();
                await _persistenceService.SaveBehaviorHistoryAsync(behaviorHistory).ConfigureAwait(false);
            }

            // Save user preferences
            if (_preferenceTracker != null)
            {
                var preferences = _preferenceTracker.ExportData();
                await _persistenceService.SaveUserPreferencesAsync(preferences).ConfigureAwait(false);
            }

            // Save statistics
            var stats = new PersistentStatistics
            {
                TotalCycles = _orchestrator.TotalCycles,
                TotalActions = _orchestrator.TotalActions,
                TotalConflicts = _orchestrator.TotalConflicts,
                FirstStart = _firstStart,
                LastUpdate = DateTime.UtcNow,
                TotalUptime = _totalUptime + _orchestrator.UpTime
            };
            await _persistenceService.SaveStatisticsAsync(stats).ConfigureAwait(false);

            // Save battery history
            var batteryHistory = _contextStore.GetBatteryHistory();
            await _persistenceService.SaveBatteryHistoryAsync(batteryHistory).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Persisted data saved successfully");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save persisted data", ex);
        }
    }

    private void StartAutoSave()
    {
        _persistenceService.StartAutoSave(
            getBehaviorHistory: () => _behaviorAnalyzer?.GetHistory() ?? new List<BehaviorDataPoint>(),
            getUserPreferences: () => _preferenceTracker?.ExportData() ?? new UserPreferencesData(),
            getStatistics: () => new PersistentStatistics
            {
                TotalCycles = _orchestrator.TotalCycles,
                TotalActions = _orchestrator.TotalActions,
                TotalConflicts = _orchestrator.TotalConflicts,
                FirstStart = _firstStart,
                LastUpdate = DateTime.UtcNow,
                TotalUptime = _totalUptime + _orchestrator.UpTime
            },
            getBatteryHistory: () => _contextStore.GetBatteryHistory()
        );
    }
}

/// <summary>
/// Orchestrator statistics for diagnostics UI
/// </summary>
public class OrchestratorStatistics
{
    public bool IsRunning { get; set; }
    public long TotalCycles { get; set; }
    public long TotalActions { get; set; }
    public long TotalConflicts { get; set; }
    public TimeSpan UpTime { get; set; }
    public int RegisteredAgents { get; set; }
    public int BehaviorDataPoints { get; set; }
    public int LearnedPreferences { get; set; }
    public long DataSizeKB { get; set; }

    public override string ToString()
    {
        return $"""
            Resource Orchestrator Statistics:
            Status: {(IsRunning ? "RUNNING" : "STOPPED")}
            Uptime: {UpTime:hh\:mm\:ss}
            Total Optimization Cycles: {TotalCycles:N0}
            Total Actions Executed: {TotalActions:N0}
            Total Conflicts Resolved: {TotalConflicts:N0}
            Registered Agents: {RegisteredAgents}
            Average Actions/Cycle: {(TotalCycles > 0 ? (double)TotalActions / TotalCycles : 0):F2}

            Learning System:
            Behavior Data Points: {BehaviorDataPoints:N0}
            Learned Preferences: {LearnedPreferences}
            Persisted Data Size: {DataSizeKB:N0} KB
            """;
    }
}
