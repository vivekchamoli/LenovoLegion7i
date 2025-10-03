using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

/// <summary>
/// Phase 4: Reactive event-based sensor controller
/// Eliminates polling overhead by using WMI events
/// Note: Reactive Extensions integration requires System.Reactive NuGet package
/// </summary>
public class ReactiveSensorsController : ISensorsController, IDisposable
{
    private readonly ISensorsController _baseController;
    private ManagementEventWatcher? _watcher;
    private bool _isInitialized;

    public event Action<SensorsData>? SensorDataChanged;

    public ReactiveSensorsController(ISensorsController baseController)
    {
        _baseController = baseController ?? throw new ArgumentNullException(nameof(baseController));
    }

    public async Task<bool> IsSupportedAsync()
    {
        return FeatureFlags.UseReactiveSensors && await _baseController.IsSupportedAsync().ConfigureAwait(false);
    }

    public async Task PrepareAsync()
    {
        await _baseController.PrepareAsync().ConfigureAwait(false);

        if (!FeatureFlags.UseReactiveSensors)
            return;

        if (_isInitialized)
            return;

        // Set up WMI event watcher for thermal changes
        var scope = "root\\WMI";
        var query = new WqlEventQuery(
            "SELECT * FROM __InstanceModificationEvent WITHIN 2 " +
            "WHERE TargetInstance ISA 'Win32_Processor' OR " +
            "TargetInstance ISA 'Win32_TemperatureProbe'"
        );

        _watcher = new ManagementEventWatcher(scope, query.QueryString);
        _watcher.EventArrived += async (sender, args) =>
        {
            try
            {
                var data = await GetDataAsync().ConfigureAwait(false);
                SensorDataChanged?.Invoke(data);
            }
            catch
            {
                // Silently ignore errors in event handler
            }
        };

        _watcher.Start();
        _isInitialized = true;

        // Emit initial value
        var initialData = await GetDataAsync().ConfigureAwait(false);
        SensorDataChanged?.Invoke(initialData);
    }

    public async Task<SensorsData> GetDataAsync()
    {
        return await _baseController.GetDataAsync().ConfigureAwait(false);
    }

    public async Task<(int cpuFanSpeed, int gpuFanSpeed)> GetFanSpeedsAsync()
    {
        return await _baseController.GetFanSpeedsAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.Stop();
            _watcher.Dispose();
            _watcher = null;
        }

        SensorDataChanged = null;
        _isInitialized = false;

        GC.SuppressFinalize(this);
    }
}
