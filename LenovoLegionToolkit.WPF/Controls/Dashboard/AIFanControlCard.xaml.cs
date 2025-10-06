using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.FanCurve;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class AIFanControlCard
{
    private readonly DispatcherTimer _updateTimer;
    private readonly AdaptiveFanCurveController? _adaptiveFanController;
    private readonly ThermalOptimizer? _thermalOptimizer;
    private readonly ResourceOrchestrator? _orchestrator;
    private readonly UserOverrideManager? _overrideManager;
    private readonly AgentCoordinator? _agentCoordinator;
    private FanProfile _currentProfile = FanProfile.Balanced;
    private string _lastAction = "Waiting for thermal data...";
    private DateTime _lastActionTime = DateTime.MinValue;
    private bool _userHasManualControl = false;

    public AIFanControlCard()
    {
        InitializeComponent();

        // Try to resolve dependencies
        try
        {
            _adaptiveFanController = IoCContainer.Resolve<AdaptiveFanCurveController>();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"AdaptiveFanCurveController not available", ex);
        }

        try
        {
            _thermalOptimizer = IoCContainer.Resolve<ThermalOptimizer>();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ThermalOptimizer not available", ex);
        }

        try
        {
            _orchestrator = IoCContainer.Resolve<ResourceOrchestrator>();

            // Subscribe to orchestrator events for real-time updates
            if (_orchestrator != null)
            {
                _orchestrator.CycleCompleted += OnOrchestratorCycleCompleted;
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ResourceOrchestrator not available", ex);
        }

        // FIX #1: Resolve UserOverrideManager for manual control
        try
        {
            _overrideManager = IoCContainer.Resolve<UserOverrideManager>();
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"UserOverrideManager resolved - manual fan control will be respected");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"UserOverrideManager not available - manual overrides will not work", ex);
        }

        // FIX #3: Resolve AgentCoordinator for broadcasting override signals
        try
        {
            _agentCoordinator = IoCContainer.Resolve<AgentCoordinator>();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"AgentCoordinator not available", ex);
        }

        // Setup update timer (1 Hz)
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Setup button click handlers
        _quietModeButton.Click += async (s, e) => await SetFanProfileAsync(FanProfile.Quiet);
        _balancedModeButton.Click += async (s, e) => await SetFanProfileAsync(FanProfile.Balanced);
        _maxCoolingButton.Click += async (s, e) => await SetFanProfileAsync(FanProfile.MaxPerformance);

        Loaded += AIFanControlCard_Loaded;
        Unloaded += AIFanControlCard_Unloaded;
    }

    private void AIFanControlCard_Loaded(object sender, RoutedEventArgs e)
    {
        _updateTimer.Start();
        _ = UpdateDisplayAsync();
    }

    private void AIFanControlCard_Unloaded(object sender, RoutedEventArgs e)
    {
        _updateTimer.Stop();

        // Unsubscribe from orchestrator events
        if (_orchestrator != null)
        {
            _orchestrator.CycleCompleted -= OnOrchestratorCycleCompleted;
        }
    }

    private async void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        await UpdateDisplayAsync();
    }

    private async Task UpdateDisplayAsync()
    {
        try
        {
            // Update AI status badge
            if (FeatureFlags.UseAdaptiveFanCurves && _adaptiveFanController != null)
            {
                _aiStatusBadge.Content = "Learning Active";
            }
            else
            {
                _aiStatusBadge.Content = "Disabled";
            }

            // Update fan profile badge
            _fanProfileBadge.Content = _currentProfile switch
            {
                FanProfile.Quiet => "Quiet Mode",
                FanProfile.Balanced => "Balanced",
                FanProfile.Aggressive => "Aggressive",
                FanProfile.MaxPerformance => "Max Cooling",
                _ => "Unknown"
            };

            // Update learning progress
            if (_adaptiveFanController != null)
            {
                var stats = _adaptiveFanController.GetLearningStats();
                var dataPoints = stats.TotalDataPoints;

                _learningProgressBar.Value = Math.Min(500, dataPoints);
                _learningLabel.Content = $"{dataPoints} / 500 samples";

                if (stats.HasSufficientData)
                {
                    _aiStatusBadge.Content = $"Learning Active ({stats.AverageCoolingEffectiveness:F0}% eff)";
                }
                else if (dataPoints > 0)
                {
                    _aiStatusBadge.Content = $"Collecting Data ({dataPoints}/50)";
                }
                else
                {
                    _aiStatusBadge.Content = "Ready to Learn";
                }
            }
            else
            {
                _learningProgressBar.Value = 0;
                _learningLabel.Content = "Adaptive learning disabled";
                _aiStatusBadge.Content = "Disabled";
            }

            // FIX #2: Update last action with override expiration warning
            var timeSinceLastAction = DateTime.UtcNow - _lastActionTime;

            // Check for active user override and show expiration warning
            if (_overrideManager != null && _userHasManualControl)
            {
                var activeOverrides = _overrideManager.GetActiveOverrides();
                var fanOverride = activeOverrides.FirstOrDefault(o => o.Control == "FAN_PROFILE");

                if (fanOverride != null)
                {
                    var timeRemaining = fanOverride.ExpiresAt - DateTime.Now;

                    if (timeRemaining.TotalMinutes <= 2 && timeRemaining.TotalMinutes > 0)
                    {
                        // CRITICAL: Override expires in 2 minutes or less - show warning
                        _lastActionLabel.Content = $"⚠️ Manual control expires in {timeRemaining.TotalMinutes:F0}m {timeRemaining.Seconds}s";
                        _lastActionLabel.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                    }
                    else if (timeRemaining.TotalMinutes > 0)
                    {
                        // Override active - show remaining time
                        _lastActionLabel.Content = $"✓ Manual control active ({timeRemaining.TotalMinutes:F0}m remaining)";
                        _lastActionLabel.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                    }
                    else
                    {
                        // Override just expired
                        _lastActionLabel.Content = "Manual control expired - AI resumed";
                        _lastActionLabel.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                        _userHasManualControl = false;
                    }
                }
                else
                {
                    // Override expired
                    _lastActionLabel.Content = "Manual control expired - AI resumed";
                    _lastActionLabel.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                    _userHasManualControl = false;
                }
            }
            else
            {
                // No active override - show last action timestamp
                if (timeSinceLastAction.TotalSeconds < 60)
                {
                    _lastActionLabel.Content = $"{_lastAction} ({timeSinceLastAction.TotalSeconds:F0}s ago)";
                }
                else if (timeSinceLastAction.TotalMinutes < 60)
                {
                    _lastActionLabel.Content = $"{_lastAction} ({timeSinceLastAction.TotalMinutes:F0}m ago)";
                }
                else
                {
                    _lastActionLabel.Content = _lastAction;
                }
                _lastActionLabel.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }

            // Highlight active profile button
            _quietModeButton.Appearance = _currentProfile == FanProfile.Quiet
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
            _balancedModeButton.Appearance = _currentProfile == FanProfile.Balanced
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
            _maxCoolingButton.Appearance = _currentProfile == FanProfile.MaxPerformance
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to update AI fan control display", ex);
        }
    }

    private async Task SetFanProfileAsync(FanProfile profile)
    {
        try
        {
            if (_thermalOptimizer == null)
                return;

            _currentProfile = profile;
            _lastAction = $"Switched to {profile} profile";
            _lastActionTime = DateTime.UtcNow;

            // Apply fan profile
            await _thermalOptimizer.ApplyFanProfileAsync(profile);

            // FIX #1: Register user override for 30 minutes to prevent AI from overriding
            if (_overrideManager != null)
            {
                _overrideManager.SetOverride(
                    control: "FAN_PROFILE",
                    value: profile,
                    duration: TimeSpan.FromMinutes(30)
                );

                _userHasManualControl = true;

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"User override registered: FAN_PROFILE = {profile} for 30 minutes");
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WARNING: UserOverrideManager not available - AI may override user's fan setting!");
            }

            // FIX #3: Broadcast coordination signal so all agents know user has taken control
            if (_agentCoordinator != null)
            {
                _agentCoordinator.BroadcastSignal(new CoordinationSignal
                {
                    Type = CoordinationType.UserOverride,
                    SourceAgent = "AIFanControlCard",
                    Timestamp = DateTime.Now,
                    Data = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Control"] = "FAN_PROFILE",
                        ["Value"] = profile,
                        ["Duration"] = "30 minutes"
                    }
                });

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Broadcast UserOverride signal to all agents");
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"User manually set fan profile to {profile}");

            await UpdateDisplayAsync();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set fan profile to {profile}", ex);

            _lastAction = $"Failed to apply {profile} profile";
            _lastActionTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Handle orchestrator optimization cycle completion
    /// Extract fan-related actions and update UI
    /// </summary>
    private void OnOrchestratorCycleCompleted(object? sender, OptimizationCycleCompleted e)
    {
        try
        {
            // Look for fan-related actions in the execution result
            if (e.ExecutionResult?.ExecutedActions == null)
                return;

            foreach (var action in e.ExecutionResult.ExecutedActions)
            {
                if (action.Target.Contains("FAN", StringComparison.OrdinalIgnoreCase))
                {
                    var actionDescription = action.Target switch
                    {
                        "FAN_PROFILE" => $"AI set fan profile to {action.Value}",
                        "FAN_SPEED_CPU" => $"AI adjusted CPU fan to {action.Value}",
                        "FAN_SPEED_GPU" => $"AI adjusted GPU fan to {action.Value}",
                        "FAN_FULL_SPEED" => "AI enabled full-speed cooling",
                        _ => $"AI thermal action: {action.Target}"
                    };

                    // Extract fan profile if available
                    FanProfile? detectedProfile = null;
                    if (action.Value is FanProfile profile)
                    {
                        detectedProfile = profile;
                    }

                    // Update UI on dispatcher thread
                    Dispatcher.InvokeAsync(() =>
                    {
                        _lastAction = actionDescription;
                        _lastActionTime = DateTime.UtcNow;

                        if (detectedProfile.HasValue)
                            _currentProfile = detectedProfile.Value;

                        _ = UpdateDisplayAsync();
                    });

                    // Only process the first fan action per cycle
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to process orchestrator cycle completion", ex);
        }
    }

    /// <summary>
    /// External method to notify of AI fan actions
    /// Can be called from ResourceOrchestrator when thermal actions are executed
    /// </summary>
    public void NotifyFanAction(string action, FanProfile? newProfile = null)
    {
        _lastAction = action;
        _lastActionTime = DateTime.UtcNow;

        if (newProfile.HasValue)
            _currentProfile = newProfile.Value;

        Dispatcher.InvokeAsync(UpdateDisplayAsync);
    }
}
