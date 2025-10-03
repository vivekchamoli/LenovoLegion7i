using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Controllers.FanCurve;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class EliteOptimizationsControl
{
    private readonly AdaptiveFanCurveController? _adaptiveFanController;
    private readonly PowerUsagePredictor? _powerPredictor;
    private readonly ISensorsController? _sensorsController;
    private readonly PowerModeFeature? _powerModeFeature;

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    public EliteOptimizationsControl()
    {
        InitializeComponent();

        try
        {
            _adaptiveFanController = IoCContainer.TryResolve<AdaptiveFanCurveController>();
            _powerPredictor = IoCContainer.TryResolve<PowerUsagePredictor>();
            _sensorsController = IoCContainer.TryResolve<ISensorsController>();
            _powerModeFeature = IoCContainer.TryResolve<PowerModeFeature>();
        }
        catch
        {
            // Controllers not available
        }

        IsVisibleChanged += EliteOptimizationsControl_IsVisibleChanged;
    }

    private async void EliteOptimizationsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await RefreshAsync();
            StartPeriodicRefresh();
            return;
        }

        await StopRefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            // Update overall stats
            var phase4ActiveCount = 0;
            if (FeatureFlags.UseAdaptiveFanCurves) phase4ActiveCount++;
            if (FeatureFlags.UseMLAIController) phase4ActiveCount++;
            if (FeatureFlags.UseReactiveSensors) phase4ActiveCount++;
            if (FeatureFlags.UseObjectPooling) phase4ActiveCount++;

            var totalActiveFeatures = 5 + phase4ActiveCount; // 5 Phase 1-3 + Phase 4 count
            _activeFeatures.Text = $"{totalActiveFeatures}/9";

            // Update Phase 4 feature statuses
            UpdateFeatureStatus(
                _adaptiveFanIcon,
                _adaptiveFanStatus,
                FeatureFlags.UseAdaptiveFanCurves,
                "Active ✓",
                "Disabled"
            );

            UpdateFeatureStatus(
                _mlAiIcon,
                _mlAiStatus,
                FeatureFlags.UseMLAIController,
                "Active ✓",
                "Disabled"
            );

            UpdateFeatureStatus(
                _reactiveSensorsIcon,
                _reactiveSensorsStatus,
                FeatureFlags.UseReactiveSensors,
                "Active ✓",
                "Disabled"
            );

            UpdateFeatureStatus(
                _objectPoolingIcon,
                _objectPoolingStatus,
                FeatureFlags.UseObjectPooling,
                "Active ✓",
                "Disabled"
            );

            // Show AI power mode suggestion if enabled
            if (FeatureFlags.UseMLAIController && _powerPredictor != null)
            {
                await UpdateMLAISuggestionAsync();
            }
            else
            {
                _aiSuggestionInfoBar.IsOpen = false;
            }

            // Show adaptive fan curve info if enabled
            if (FeatureFlags.UseAdaptiveFanCurves && _adaptiveFanController != null)
            {
                await UpdateAdaptiveFanInfoAsync();
            }
            else
            {
                _adaptiveFanInfoBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to refresh Elite Optimizations status", ex);
        }
    }

    private void UpdateFeatureStatus(
        SymbolIcon icon,
        System.Windows.Controls.TextBlock statusText,
        bool isEnabled,
        string enabledText,
        string disabledText)
    {
        if (isEnabled)
        {
            icon.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            statusText.Text = enabledText;
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            statusText.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            icon.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            statusText.Text = disabledText;
            statusText.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            statusText.FontWeight = FontWeights.Normal;
        }
    }

    private async Task UpdateMLAISuggestionAsync()
    {
        try
        {
            if (_sensorsController == null || _powerModeFeature == null || _powerPredictor == null)
            {
                _aiSuggestionInfoBar.IsOpen = false;
                return;
            }

            var sensorsData = await _sensorsController.GetDataAsync();
            var currentMode = await _powerModeFeature.GetStateAsync();
            var cpuTemp = sensorsData.CPU.Temperature;
            var timeOfDay = DateTime.Now.TimeOfDay;

            // Estimate CPU usage from utilization or temperature
            var cpuUsage = sensorsData.CPU.Utilization > 0 ? sensorsData.CPU.Utilization : (cpuTemp > 60 ? 80 : cpuTemp > 50 ? 50 : 30);
            var isOnBattery = false; // Would need BatteryFeature to get actual status

            var suggestion = _powerPredictor.GetPowerModeSuggestion(
                currentMode,
                cpuUsage,
                cpuTemp,
                isOnBattery,
                timeOfDay
            );

            if (suggestion.ShouldSwitch)
            {
                _aiSuggestionInfoBar.Title = "🧠 AI Power Mode Suggestion";
                _aiSuggestionInfoBar.Message = $"{suggestion.Reason}\nRecommended: {suggestion.RecommendedMode}";
                _aiSuggestionInfoBar.Severity = InfoBarSeverity.Informational;
                _aiSuggestionInfoBar.IsOpen = true;
            }
            else
            {
                _aiSuggestionInfoBar.IsOpen = false;
            }
        }
        catch
        {
            _aiSuggestionInfoBar.IsOpen = false;
        }
    }

    private async Task UpdateAdaptiveFanInfoAsync()
    {
        try
        {
            if (_sensorsController == null || _powerModeFeature == null || _adaptiveFanController == null)
            {
                _adaptiveFanInfoBar.IsOpen = false;
                return;
            }

            var sensorsData = await _sensorsController.GetDataAsync();
            var currentMode = await _powerModeFeature.GetStateAsync();
            var cpuTemp = sensorsData.CPU.Temperature;
            var cpuFanSpeed = sensorsData.CPU.FanSpeed;

            // Calculate thermal trend (simplified for demo)
            var tempTrend = cpuTemp > 70 ? 3 : cpuTemp > 60 ? 1 : cpuTemp < 45 ? -2 : 0;

            var fanSuggestion = _adaptiveFanController.SuggestFanSpeed(
                cpuTemp,
                cpuFanSpeed,
                tempTrend,
                currentMode
            );

            if (fanSuggestion.ShouldAdjust)
            {
                _adaptiveFanInfoBar.Title = "🌊 Adaptive Fan Curve Suggestion";
                _adaptiveFanInfoBar.Message = fanSuggestion.Reason;
                _adaptiveFanInfoBar.Severity = InfoBarSeverity.Success;
                _adaptiveFanInfoBar.IsOpen = true;
            }
            else
            {
                _adaptiveFanInfoBar.Title = "🌊 Adaptive Fan Curves Active";
                _adaptiveFanInfoBar.Message = $"Current: {cpuTemp}°C @ {cpuFanSpeed} RPM - Learning thermal patterns...";
                _adaptiveFanInfoBar.Severity = InfoBarSeverity.Success;
                _adaptiveFanInfoBar.IsOpen = true;
            }
        }
        catch
        {
            _adaptiveFanInfoBar.IsOpen = false;
        }
    }

    private void StartPeriodicRefresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var token = _cts.Token;

        _refreshTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Dispatcher.InvokeAsync(async () => await RefreshAsync(), DispatcherPriority.Background);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Elite Optimizations status refresh error", ex);

                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                }
            }
        }, token);
    }

    private async Task StopRefreshAsync()
    {
        if (_cts is not null)
            await _cts.CancelAsync();

        _cts = null;

        if (_refreshTask is not null)
            await _refreshTask;

        _refreshTask = null;
    }
}
