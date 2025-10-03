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

public partial class Phase4StatusControl
{
    private readonly AdaptiveFanCurveController? _adaptiveFanController;
    private readonly PowerUsagePredictor? _powerPredictor;
    private readonly ISensorsController _sensorsController;
    private readonly PowerModeFeature _powerModeFeature;

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    public Phase4StatusControl()
    {
        InitializeComponent();

        try
        {
            _adaptiveFanController = IoCContainer.TryResolve<AdaptiveFanCurveController>();
            _powerPredictor = IoCContainer.TryResolve<PowerUsagePredictor>();
            _sensorsController = IoCContainer.Resolve<ISensorsController>();
            _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
        }
        catch
        {
            // Controllers not available
        }

        IsVisibleChanged += Phase4StatusControl_IsVisibleChanged;
    }

    private async void Phase4StatusControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
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
            // Check if any Phase 4 features are enabled
            var anyEnabled = FeatureFlags.UseAdaptiveFanCurves ||
                            FeatureFlags.UseMLAIController ||
                            FeatureFlags.UseReactiveSensors ||
                            FeatureFlags.UseObjectPooling;

            if (!anyEnabled)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;

            // Update Adaptive Fan Curves status
            UpdateFeatureStatus(
                _adaptiveFanIcon,
                _adaptiveFanStatus,
                FeatureFlags.UseAdaptiveFanCurves,
                "Active - Learning thermal patterns",
                "Disabled"
            );

            // Update ML/AI Power Predictor status
            UpdateFeatureStatus(
                _mlAiIcon,
                _mlAiStatus,
                FeatureFlags.UseMLAIController,
                "Active - Analyzing usage patterns",
                "Disabled"
            );

            // Update Reactive Sensors status
            UpdateFeatureStatus(
                _reactiveSensorsIcon,
                _reactiveSensorsStatus,
                FeatureFlags.UseReactiveSensors,
                "Active - Event-based updates",
                "Disabled"
            );

            // Update Object Pooling status
            UpdateFeatureStatus(
                _objectPoolingIcon,
                _objectPoolingStatus,
                FeatureFlags.UseObjectPooling,
                "Active - Reducing GC pressure",
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
                Log.Instance.Trace($"Failed to refresh Phase 4 status", ex);
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
        }
        else
        {
            icon.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            statusText.Text = disabledText;
            statusText.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
        }
    }

    private async Task UpdateMLAISuggestionAsync()
    {
        try
        {
            var sensorsData = await _sensorsController.GetDataAsync();
            var currentMode = await _powerModeFeature.GetStateAsync();
            var isOnBattery = sensorsData.IsOnBattery;
            var cpuTemp = sensorsData.CpuTemperature?.FirstOrDefault()?.Value ?? 0;
            var timeOfDay = DateTime.Now.TimeOfDay;

            // For demo purposes, estimate CPU usage (would need real data)
            var cpuUsage = cpuTemp > 60 ? 80 : cpuTemp > 50 ? 50 : 30;

            var suggestion = _powerPredictor.GetPowerModeSuggestion(
                currentMode,
                cpuUsage,
                cpuTemp,
                isOnBattery,
                timeOfDay
            );

            if (suggestion.ShouldSwitch)
            {
                _aiSuggestionInfoBar.Title = "AI Power Mode Suggestion";
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
            var sensorsData = await _sensorsController.GetDataAsync();
            var currentMode = await _powerModeFeature.GetStateAsync();
            var cpuTemp = sensorsData.CpuTemperature?.FirstOrDefault()?.Value ?? 0;
            var cpuFanSpeed = sensorsData.CpuFanSpeed?.Value ?? 0;

            // For demo, calculate a simple trend (would need historical data)
            var tempTrend = cpuTemp > 70 ? 3 : cpuTemp > 60 ? 1 : cpuTemp < 45 ? -2 : 0;

            var fanSuggestion = _adaptiveFanController.SuggestFanSpeed(
                cpuTemp,
                cpuFanSpeed,
                tempTrend,
                currentMode
            );

            if (fanSuggestion.ShouldAdjust)
            {
                _adaptiveFanInfoBar.Title = "Adaptive Fan Curve Suggestion";
                _adaptiveFanInfoBar.Message = fanSuggestion.Reason;
                _adaptiveFanInfoBar.Severity = InfoBarSeverity.Success;
                _adaptiveFanInfoBar.IsOpen = true;
            }
            else
            {
                _adaptiveFanInfoBar.Title = "Adaptive Fan Curves";
                _adaptiveFanInfoBar.Message = "Fan speed is optimal. Learning thermal patterns...";
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
                        Log.Instance.Trace($"Phase 4 status refresh error", ex);

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
