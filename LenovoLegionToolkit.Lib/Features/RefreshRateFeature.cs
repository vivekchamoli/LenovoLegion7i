using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.Native.DeviceContext;

namespace LenovoLegionToolkit.Lib.Features;

public class RefreshRateFeature : IFeature<RefreshRate>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<RefreshRate[]> GetAllStatesAsync()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Getting all refresh rates...");

        var display = InternalDisplay.Get();
        if (display is null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Built in display not found");

            return Task.FromResult(Array.Empty<RefreshRate>());
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Built in display found: {display}");

        var currentSettings = display.CurrentSetting;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        var possibleSettings = display.GetPossibleSettings().ToArray();

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Total possible settings from Windows: {possibleSettings.Length}");

            // Group by resolution and color depth to show all available modes
            var grouped = possibleSettings
                .GroupBy(s => new { s.Resolution, s.ColorDepth })
                .OrderByDescending(g => g.Key.Resolution.Width);

            foreach (var group in grouped)
            {
                var frequencies = string.Join(", ", group.Select(s => $"{s.Frequency}Hz"));
                Log.Instance.Trace($"  {group.Key.Resolution} @ {group.Key.ColorDepth}bpp: {frequencies}");
            }

            Log.Instance.Trace($"Filtering to match: Resolution={currentSettings.Resolution}, ColorDepth={currentSettings.ColorDepth}");
        }

        var result = possibleSettings
            .Where(dps => Match(dps, currentSettings))
            .Select(dps => dps.Frequency)
            .Distinct()
            .OrderBy(freq => freq)
            .Select(freq => new RefreshRate(freq))
            .ToArray();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Possible refresh rates are {string.Join(", ", result)}");

        return Task.FromResult(result);
    }

    public Task<RefreshRate> GetStateAsync()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Getting current refresh rate...");

        var display = InternalDisplay.Get();
        if (display is null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Built in display not found");

            return Task.FromResult(default(RefreshRate));
        }

        var currentSettings = display.CurrentSetting;
        var result = new RefreshRate(currentSettings.Frequency);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Current refresh rate is {result} [currentSettings={currentSettings.ToExtendedString()}]");

        return Task.FromResult(result);
    }

    public Task SetStateAsync(RefreshRate state)
    {
        var display = InternalDisplay.Get();
        if (display is null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Built in display not found");
            throw new InvalidOperationException("Built in display not found");
        }

        var currentSettings = display.CurrentSetting;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        if (currentSettings.Frequency == state.Frequency)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Frequency already set to {state.Frequency}");

            return Task.CompletedTask;
        }

        var possibleSettings = display.GetPossibleSettings();

        // Try to find setting with same color depth first (preferred)
        var newSettings = possibleSettings
            .Where(dps => Match(dps, currentSettings))
            .Where(dps => dps.Frequency == state.Frequency)
            .Select(dps => new DisplaySetting(dps, currentSettings.Position, currentSettings.Orientation, DisplayFixedOutput.Default))
            .FirstOrDefault();

        // If not found, try to find ANY setting with desired frequency (may change color depth)
        if (newSettings is null)
        {
            newSettings = possibleSettings
                .Where(dps => dps.Resolution == currentSettings.Resolution)
                .Where(dps => dps.IsInterlaced == currentSettings.IsInterlaced)
                .Where(dps => dps.Frequency == state.Frequency)
                .Where(dps => !dps.IsTooSmall())
                .Select(dps => new DisplaySetting(dps, currentSettings.Position, currentSettings.Orientation, DisplayFixedOutput.Default))
                .FirstOrDefault();

            if (newSettings is not null && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Found settings at different color depth: {newSettings.ToExtendedString()}");
        }

        if (newSettings is not null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Setting display to {newSettings.ToExtendedString()}...");

            display.SetSettingsUsingPathInfo(newSettings);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Display set to {newSettings.ToExtendedString()}");
        }
        else
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Could not find matching settings for frequency {state}");
        }

        return Task.CompletedTask;
    }

    private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
    {
        if (dps.IsTooSmall())
            return false;

        var result = true;
        result &= dps.Resolution == ds.Resolution;
        result &= dps.ColorDepth == ds.ColorDepth;
        result &= dps.IsInterlaced == ds.IsInterlaced;
        return result;
    }
}
