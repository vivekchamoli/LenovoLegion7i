# Refresh Rate Diagnostic Logging Implementation - v6.2.0

**Date**: October 3, 2025
**Issue**: Enhanced diagnostic logging to identify refresh rate limitations
**Status**: ✅ **IMPLEMENTED**

---

## What Was Implemented

Added enhanced diagnostic logging to `RefreshRateFeature.cs` to show exactly what Windows reports for available display modes, grouped by resolution and color depth.

### File Modified

**LenovoLegionToolkit.Lib\Features\RefreshRateFeature.cs** (lines 38-56)

### Code Added

```csharp
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
```

---

## How to Use

### Enable Trace Logging

1. Open Legion Toolkit settings
2. Enable trace-level logging
3. Restart application

### View Diagnostic Output

1. Navigate to Dashboard → Display section
2. Open log file: `%LocalAppData%\LenovoLegionToolkit\Logs\[latest].log`
3. Search for: `"Total possible settings from Windows"`

### Example Output

#### Scenario 1: HDR Enabled (10-bit color limiting refresh rates)

```
[TRACE] Getting all refresh rates...
[TRACE] Built in display found: \\.\DISPLAY1
[TRACE] Current built in display settings: 2560x1600 @ 30Hz @ 32bpp (0, Rotate0, Unspecified)
[TRACE] Total possible settings from Windows: 24
[TRACE]   2560x1600 @ 32bpp: 30Hz, 60Hz
[TRACE]   2560x1600 @ 24bpp: 30Hz, 60Hz, 120Hz, 165Hz
[TRACE]   1920x1080 @ 24bpp: 30Hz, 60Hz, 120Hz
[TRACE] Filtering to match: Resolution=2560x1600, ColorDepth=32
[TRACE] Possible refresh rates are 30 Hz, 60 Hz
```

**Analysis**: Windows reports higher refresh rates at 24bpp (8-bit), but current mode is 32bpp (10-bit). **Solution**: Disable HDR.

#### Scenario 2: Driver Issue (Windows only reports 30Hz)

```
[TRACE] Getting all refresh rates...
[TRACE] Built in display found: \\.\DISPLAY1
[TRACE] Current built in display settings: 2560x1600 @ 30Hz @ 24bpp (0, Rotate0, Unspecified)
[TRACE] Total possible settings from Windows: 2
[TRACE]   2560x1600 @ 24bpp: 30Hz
[TRACE] Filtering to match: Resolution=2560x1600, ColorDepth=24
[TRACE] Possible refresh rates are 30 Hz
```

**Analysis**: Windows only reports 30Hz even at 24bpp (8-bit). **Solution**: Reinstall graphics driver.

---

## Interpreting Results

### Color Depth Values

| Value | Meaning | Typical Use |
|-------|---------|-------------|
| **24bpp** | 8-bit color (24-bit RGB) | Standard SDR, higher refresh rates |
| **32bpp** | 10-bit color (30-bit RGB + 2-bit padding) | HDR, limited refresh rates |

### Bandwidth Calculation

**Example: 2560x1600 @ 165Hz**

- **8-bit (24bpp)**: 2560 × 1600 × 165 × 24 = 16.1 Gbps ✅ (within HDMI 2.0 ~18 Gbps)
- **10-bit (32bpp)**: 2560 × 1600 × 165 × 32 = 21.5 Gbps ❌ (exceeds HDMI 2.0)

**Result**: 10-bit color limits to 30Hz or 60Hz due to bandwidth constraints.

---

## Diagnostic Flowchart

```
Start
  ↓
Check logs for "Total possible settings from Windows"
  ↓
[Is there only ONE setting reported?]
  ├─ YES → Driver issue - Reinstall graphics driver
  └─ NO → Continue
       ↓
[Are higher refresh rates shown at DIFFERENT color depth?]
  ├─ YES → Color depth limiting
  │        ↓
  │        [Is current at 32bpp/30bpp/10-bit?]
  │        ├─ YES → HDR is enabled
  │        │        Solution: Disable HDR in Windows Settings
  │        └─ NO → Check NVIDIA Control Panel color settings
  └─ NO → Continue
       ↓
[Are higher resolutions available with higher refresh?]
  ├─ YES → Current resolution not supported at high refresh
  │        Solution: Use lower resolution or check cable/port
  └─ NO → Unknown issue - Check cable, port, monitor specs
```

---

## Common Issues Identified

### Issue 1: HDR Enabled ⚠️ MOST COMMON

**Symptoms**:
- Log shows modes at 24bpp: 30Hz, 60Hz, 120Hz, 165Hz
- Log shows modes at 32bpp: 30Hz, 60Hz
- Current mode is 32bpp

**Root Cause**: HDR enabled → 10-bit color → Bandwidth limited

**Solution**:
1. Windows Settings → Display → HDR → Turn OFF
2. Or accept 60Hz max with HDR enabled
3. Or upgrade to HDMI 2.1 / DisplayPort 1.4

### Issue 2: NVIDIA Color Depth Setting

**Symptoms**:
- Log shows only 32bpp modes
- HDR is disabled

**Root Cause**: NVIDIA Control Panel set to 10-bit color manually

**Solution**:
1. NVIDIA Control Panel → Change resolution
2. Output color depth → Set to "8 bpc" (not "10 bpc")
3. Apply

### Issue 3: Driver Not Exposing Modes

**Symptoms**:
- Log shows very few total settings (< 5)
- Only 30Hz reported at all color depths

**Root Cause**: Graphics driver issue

**Solution**:
1. DDU (Display Driver Uninstaller) in Safe Mode
2. Clean install latest NVIDIA/AMD driver
3. Restart

---

## Testing the Diagnostic Output

### Test 1: Verify Logging Works

1. Enable trace logging
2. Navigate to Dashboard
3. Check refresh rate dropdown
4. Open log file
5. Search for "Total possible settings from Windows"

**Expected**: Should see diagnostic output with grouped modes

### Test 2: Test HDR Impact

1. Note current log output
2. Enable HDR: Windows Settings → Display → HDR
3. Wait 5 seconds (display config event)
4. Check log file again
5. Compare outputs

**Expected**: Should see modes grouped differently (32bpp vs 24bpp)

### Test 3: Test Driver Reinstall

1. Note current log output (only 30Hz shown)
2. Reinstall graphics driver (clean install)
3. Restart
4. Open toolkit, check logs

**Expected**: Should see more modes reported if driver was the issue

---

## Integration with Existing Fix

This diagnostic logging works together with the Display Refresh Fix (v6.2.0):

1. **Display config changes** → DisplayConfigurationListener fires
2. **Cache invalidates** → InternalDisplay.SetNeedsRefresh()
3. **Control refreshes** → RefreshRateControl calls RefreshAsync()
4. **Feature queries** → RefreshRateFeature.GetAllStatesAsync()
5. **Diagnostic logs** → Shows all available modes (NEW)
6. **Filtering applies** → Matches current resolution/color depth
7. **UI updates** → Dropdown shows filtered results

---

## Benefits

✅ **Identifies root cause** - Shows exactly what Windows reports
✅ **Differentiates issues** - Driver vs configuration vs hardware
✅ **User-actionable** - Clear solutions for each scenario
✅ **No performance impact** - Only logs when trace enabled
✅ **Production safe** - No behavior changes, logging only

---

## Next Steps for User

1. **Enable trace logging** in Legion Toolkit settings
2. **Reproduce issue** - Navigate to Dashboard → Display
3. **Collect log file** from `%LocalAppData%\LenovoLegionToolkit\Logs`
4. **Search for** "Total possible settings from Windows"
5. **Share output** for analysis

---

## Related Documents

- `REFRESH_RATE_REGRESSION_ANALYSIS.md` - Root cause analysis
- `DISPLAY_REFRESH_FIX_v6.2.0.md` - Display refresh race condition fix
- `REFRESH_RATE_DIAGNOSTIC.md` - Original diagnostic guide

---

**Implementation Date**: October 3, 2025
**Version**: v6.2.0
**Files Modified**: 1 (RefreshRateFeature.cs)
**Lines Changed**: 19 lines added
**Status**: ✅ **READY FOR TESTING**
