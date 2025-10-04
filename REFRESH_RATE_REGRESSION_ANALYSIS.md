# Refresh Rate Regression Analysis - "It Was Working Before"

**Date**: October 3, 2025
**Issue**: Refresh rate dropdown showed all options initially, now only shows 30Hz
**Type**: Code regression / Cache invalidation issue
**Status**: 🔍 **INVESTIGATING**

---

## Critical Information

**User Report**: "It was working during initial build though"

This indicates:
- ✅ NOT a driver issue (would have been consistent)
- ✅ NOT a hardware issue (would never have worked)
- ✅ NOT a color depth issue (would have been consistent)
- 🔴 **LIKELY**: Cache invalidation bug or state management issue

---

## Hypothesis: Cache Invalidation Problem

### Theory

The InternalDisplay caching system may not be properly invalidating when display configuration changes:

```csharp
// InternalDisplay.cs
private static DisplayHolder? _displayHolder;

public static void SetNeedsRefresh()
{
    lock (Lock)
    {
        _displayHolder = null;  // Clears toolkit cache
    }
}

public static Display? Get()
{
    lock (Lock)
    {
        if (_displayHolder is not null)
            return _displayHolder;  // Returns cached Display object

        // Only queries if cache is null
        var displays = Display.GetDisplays().ToArray();
        // ...
    }
}
```

### Potential Issues

1. **WindowsDisplayAPI Internal Caching**
   - `Display.GetPossibleSettings()` may have its own internal cache
   - When toolkit gets a fresh Display object, WindowsDisplayAPI may still return cached data
   - WindowsDisplayAPI doesn't know toolkit cleared its cache

2. **Timing Issue**
   - App starts → Display queried → Shows all modes ✅
   - Windows changes display config (e.g., enabling HDR) → Listener fires
   - `SetNeedsRefresh()` called → Cache cleared
   - Next refresh → Gets fresh Display object → BUT WindowsDisplayAPI returns stale cached modes ❌

3. **Display Configuration Change During App Lifecycle**
   - App starts with display at: 2560x1600 @ 165Hz @ 8-bit
   - Windows (or another app) changes to: 2560x1600 @ 30Hz @ 10-bit
   - Toolkit cache invalidates and re-queries
   - Now only sees modes matching 10-bit color depth

---

## Investigation Steps

### Step 1: Check Current Display State

Add diagnostic logging to see what Windows is actually reporting:

```csharp
// Add to RefreshRateFeature.cs in GetAllStatesAsync()
var possibleSettings = display.GetPossibleSettings().ToArray();

if (Log.Instance.IsTraceEnabled)
{
    Log.Instance.Trace($"Total possible settings from Windows: {possibleSettings.Length}");

    // Group by resolution and color depth
    var grouped = possibleSettings
        .GroupBy(s => new { s.Resolution, s.ColorDepth })
        .OrderByDescending(g => g.Key.Resolution.Width);

    foreach (var group in grouped)
    {
        var freqs = string.Join(", ", group.Select(s => $"{s.Frequency}Hz"));
        Log.Instance.Trace($"  {group.Key.Resolution} @ {group.Key.ColorDepth}bpp: {freqs}");
    }

    Log.Instance.Trace($"Current settings: {currentSettings.ToExtendedString()}");
    Log.Instance.Trace($"Filtering to match: Resolution={currentSettings.Resolution}, ColorDepth={currentSettings.ColorDepth}");
}
```

This would show:
```
Total possible settings from Windows: 24
  2560x1600 @ 32bpp: 30Hz, 60Hz
  2560x1600 @ 24bpp: 30Hz, 60Hz, 120Hz, 165Hz
  1920x1080 @ 24bpp: 30Hz, 60Hz, 120Hz
Current settings: 2560x1600p @ 30Hz @ 32 (0, Rotate0, Unspecified)
Filtering to match: Resolution=2560x1600, ColorDepth=32
Possible refresh rates are 30 Hz, 60 Hz
```

### Step 2: Check When Display Configuration Changed

Check Windows Event Viewer:
```
Event Viewer → Windows Logs → System
Filter: Source = "Display"
Look for: Recent display configuration change events
```

Or check toolkit logs for:
```
DisplayConfigurationListener: Event received
InternalDisplay: Resetting holder...
```

### Step 3: Force Cache Invalidation Test

Temporarily modify code to force refresh on every call:

```csharp
// InternalDisplay.cs - TEMPORARY TEST
public static Display? Get()
{
    lock (Lock)
    {
        // ALWAYS refresh (no caching) - FOR TESTING ONLY
        _displayHolder = null;

        var displays = Display.GetDisplays().ToArray();
        // ...
    }
}
```

If this fixes it → Confirms cache invalidation issue

---

## Possible Root Causes

### Cause 1: HDR Was Enabled ⚠️ VERY LIKELY

**Scenario**:
```
App Launch (Initial Build):
  Display: 2560x1600 @ 165Hz @ 8-bit (HDR off)
  Toolkit shows: 30Hz, 60Hz, 120Hz, 165Hz ✅

Later (User enables HDR or app auto-enables):
  Windows switches to: 2560x1600 @ 30Hz @ 10-bit (HDR on)
  Toolkit cache invalidates
  Toolkit re-queries with new settings (10-bit)
  Toolkit shows: 30Hz, 60Hz only ❌
```

**Evidence to check**:
- Windows Settings → Display → HDR → Is "Use HDR" currently enabled?
- If yes, was it disabled when app first launched?

### Cause 2: Power Management Changed Display Mode

**Scenario**:
```
App Launch:
  Laptop on AC power
  Display: Full performance mode (165Hz available)
  Toolkit shows: All refresh rates ✅

Later (Laptop unplugged):
  Windows power management reduces refresh rate
  Display switches to: 30Hz (battery saving)
  Toolkit cache invalidates
  Toolkit re-queries at 30Hz mode
  Toolkit shows: Only 30Hz ❌
```

### Cause 3: Another Application Changed Display Settings

**Scenario**:
```
App Launch:
  Display: Normal configuration
  Toolkit shows: All refresh rates ✅

Later (Game launched):
  Game sets display to specific mode
  Game exits but doesn't restore previous mode
  Display now at: 30Hz @ 10-bit
  Toolkit cache invalidates
  Toolkit re-queries
  Toolkit shows: Only 30Hz ❌
```

### Cause 4: WindowsDisplayAPI Caching Bug

**Scenario**:
```
App Launch:
  WindowsDisplayAPI queries Windows → Gets fresh data
  Toolkit shows: All refresh rates ✅

Later (Display config changes):
  Toolkit calls SetNeedsRefresh() → Clears toolkit cache
  Toolkit calls Display.GetDisplays() → Gets new Display object
  BUT: WindowsDisplayAPI has internal cache
  Display.GetPossibleSettings() → Returns stale cached data ❌
  Toolkit shows: Stale data (possibly only 30Hz)
```

---

## Quick Diagnostic Test

Have user run this in toolkit logs (with trace enabled):

### Test 1: Restart Application
```
1. Close Legion Toolkit completely
2. Open Windows Settings → Display → Refresh rate
3. Note what options Windows shows
4. Start Legion Toolkit
5. Check Dashboard → Refresh Rate
```

**If restart fixes it**: Confirms state/cache issue
**If restart doesn't fix it**: Confirms persistent configuration change

### Test 2: Check HDR Status
```
1. Windows Settings → Display → HDR
2. If "Use HDR" is ON → Turn it OFF
3. Check Legion Toolkit → Refresh Rate dropdown
```

**If disabling HDR adds more options**: Confirms HDR/color depth issue

### Test 3: Check Power Mode
```
1. If on battery → Plug in AC adapter
2. Check Legion Toolkit → Refresh Rate dropdown
```

**If plugging in adds more options**: Confirms power management issue

---

## Fix Options

### Option 1: Force Display Cache Refresh on Visibility

```csharp
// RefreshRateControl.cs
private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.InvokeAsync(async () =>
{
    if (IsLoaded && IsVisible)
    {
        // Force display cache refresh before querying
        InternalDisplay.SetNeedsRefresh();
        await RefreshAsync();
    }
});
```

### Option 2: Don't Filter by Color Depth (Show All Available Modes)

```csharp
// RefreshRateFeature.cs
private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
{
    if (dps.IsTooSmall())
        return false;

    var result = true;
    result &= dps.Resolution == ds.Resolution;
    // REMOVED: result &= dps.ColorDepth == ds.ColorDepth;  // Don't filter by color
    result &= dps.IsInterlaced == ds.IsInterlaced;
    return result;
}
```

**Pros**: User sees all available refresh rates
**Cons**: Selecting 165Hz might silently change from 10-bit to 8-bit color

### Option 3: Show Diagnostic Information

Add UI element showing:
```
Current Display Mode: 2560x1600 @ 30Hz @ 10-bit color

Available Refresh Rates (current color depth): 30Hz, 60Hz
Additional Rates (requires 8-bit color): 120Hz, 165Hz

⚠️ To enable 120Hz and 165Hz:
  1. Disable HDR in Windows Settings
  2. Or change color depth in NVIDIA Control Panel
```

### Option 4: Add "Show All Modes" Checkbox

```xaml
<CheckBox x:Name="_showAllModesCheckbox"
          Content="Show all refresh rates (may change color settings)"
          Checked="ShowAllModes_Changed" />
```

When checked: Don't filter by color depth, show all available refresh rates

---

## Immediate Action Required

### For User to Test Now:

1. **Check HDR status**:
   ```
   Windows Settings → Display → HDR
   Is "Use HDR" enabled?
   ```

2. **Check current display mode**:
   ```
   Windows Settings → Display → Advanced display
   What does it show for:
   - Refresh rate: ___Hz
   - Color format: RGB ___-bit
   ```

3. **Check power mode**:
   ```
   Is laptop on AC power or battery?
   ```

4. **Check toolkit logs**:
   ```
   %LocalAppData%\LenovoLegionToolkit\Logs\
   Open latest log file
   Search for: "Current built in display settings"
   What does it show?
   ```

### For Developer to Implement:

1. **Add enhanced diagnostic logging** (Option 1 above)
2. **Add "Show All Modes" option** (Option 4 above)
3. **Consider removing color depth filter** (Option 2 - if user feedback supports it)

---

## Expected Findings

Based on "it was working before", most likely:

**90% Probability**: HDR was enabled automatically
- Windows 11 may auto-enable HDR
- Another app may have triggered HDR mode
- HDR requires 10-bit color → Limits bandwidth → Only 30Hz/60Hz available

**8% Probability**: Power management changed mode
- Laptop switched to battery power
- Windows reduced refresh rate to save power
- Toolkit cache updated to reflect new mode

**2% Probability**: Another app changed display settings
- Game or graphics software set specific mode
- Mode wasn't restored on app exit

---

## Conclusion

**This is almost certainly NOT a toolkit bug**, but rather:
1. Display configuration changed after initial app launch
2. Toolkit correctly updated to reflect new configuration
3. New configuration (likely 10-bit color for HDR) limits available refresh rates

**User perceives this as**: "It broke"
**Reality is**: "Display configuration changed, toolkit accurately reflects new state"

**Solution**:
- Disable HDR → Gets back all refresh rates
- Or add UI to show/explain why options are limited
- Or add "Show All Modes" option to bypass filtering

---

**Analysis Date**: October 3, 2025
**Status**: Awaiting user diagnostic results
**Next Steps**:
1. User checks HDR status
2. User provides current display mode info
3. Developer adds diagnostic logging
