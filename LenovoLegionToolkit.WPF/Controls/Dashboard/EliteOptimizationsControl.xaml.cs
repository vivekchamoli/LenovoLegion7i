using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    private bool _manualFanControlEnabled;
    private bool _isUpdatingSliders;

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

        // Initialize UI state
        _aiAdaptiveFanToggle.IsChecked = FeatureFlags.UseAdaptiveFanCurves;
        _manualFanControlEnabled = !FeatureFlags.UseAdaptiveFanCurves;
        UpdateManualControlsState();

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
            // Update real-time sensor data
            if (_sensorsController != null)
            {
                var sensorsData = await _sensorsController.GetDataAsync();

                // Update real-time metrics
                _cpuTemp.Text = $"{sensorsData.CPU.Temperature}°C";
                _cpuFanSpeed.Text = $"{sensorsData.CPU.FanSpeed} RPM";
                _gpuFanSpeed.Text = $"{sensorsData.GPU.FanSpeed} RPM";

                // Color code CPU temperature
                if (sensorsData.CPU.Temperature > 80)
                    _cpuTemp.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                else if (sensorsData.CPU.Temperature > 70)
                    _cpuTemp.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Yellow
                else
                    _cpuTemp.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green

                // Update sliders to reflect current fan speeds (only if not manually controlling)
                if (!_manualFanControlEnabled && !_isUpdatingSliders)
                {
                    _isUpdatingSliders = true;
                    _cpuFanSlider.Value = Math.Min(100, (sensorsData.CPU.FanSpeed / 50)); // Approximate percentage
                    _gpuFanSlider.Value = Math.Min(100, (sensorsData.GPU.FanSpeed / 50)); // Approximate percentage
                    _isUpdatingSliders = false;
                }
            }

            // Update feature statuses
            UpdateFeatureStatus(
                _mlAiIcon,
                _mlAiStatus,
                FeatureFlags.UseMLAIController,
                "Active ✓",
                "Disabled"
            );

            UpdateFeatureStatus(
                _adaptiveFanIcon,
                _adaptiveFanStatus,
                FeatureFlags.UseAdaptiveFanCurves,
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
                Log.Instance.Trace($"Failed to refresh AI/ML Performance System status", ex);
        }
    }

    private void UpdateFeatureStatus(
        SymbolIcon icon,
        TextBlock statusText,
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
                _adaptiveFanInfoBar.Title = "Adaptive Fan Learning Active";
                _adaptiveFanInfoBar.Message = fanSuggestion.Reason;
                _adaptiveFanInfoBar.Severity = InfoBarSeverity.Success;
                _adaptiveFanInfoBar.IsOpen = true;
            }
            else
            {
                _adaptiveFanInfoBar.Title = "Adaptive Fan Curves Active";
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

    private void AiAdaptiveFanToggle_Checked(object sender, RoutedEventArgs e)
    {
        _manualFanControlEnabled = false;
        UpdateManualControlsState();

        // Enable adaptive fan curves feature flag
        Environment.SetEnvironmentVariable("LLT_FEATURE_ADAPTIVEFANCURVES", "true", EnvironmentVariableTarget.User);

        _adaptiveFanInfoBar.Title = "AI/ML Adaptive Fan Control Enabled";
        _adaptiveFanInfoBar.Message = "Fan curves will be automatically optimized based on thermal patterns.";
        _adaptiveFanInfoBar.Severity = InfoBarSeverity.Success;
        _adaptiveFanInfoBar.IsOpen = true;
    }

    private void AiAdaptiveFanToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _manualFanControlEnabled = true;
        UpdateManualControlsState();

        // Disable adaptive fan curves feature flag
        Environment.SetEnvironmentVariable("LLT_FEATURE_ADAPTIVEFANCURVES", "false", EnvironmentVariableTarget.User);

        _adaptiveFanInfoBar.IsOpen = false;
    }

    private void UpdateManualControlsState()
    {
        _manualFanControls.IsEnabled = _manualFanControlEnabled;
        _manualFanControls.Opacity = _manualFanControlEnabled ? 1.0 : 0.5;
    }

    private void CpuFanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_cpuFanSliderValue == null || _isUpdatingSliders) return;

        var value = (int)e.NewValue;
        _cpuFanSliderValue.Text = $"{value}%";

        if (_manualFanControlEnabled)
        {
            // TODO: Apply manual fan speed control via fan controller
            // This would require access to the actual fan control API
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Manual CPU fan control: {value}%");
        }
    }

    private void GpuFanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_gpuFanSliderValue == null || _isUpdatingSliders) return;

        var value = (int)e.NewValue;
        _gpuFanSliderValue.Text = $"{value}%";

        if (_manualFanControlEnabled)
        {
            // TODO: Apply manual fan speed control via fan controller
            // This would require access to the actual fan control API
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Manual GPU fan control: {value}%");
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
                    await Task.Delay(TimeSpan.FromSeconds(2), token); // Faster refresh for real-time data
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"AI/ML Performance System refresh error", ex);

                    await Task.Delay(TimeSpan.FromSeconds(5), token);
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
