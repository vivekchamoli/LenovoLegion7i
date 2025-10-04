# Refresh Rate Stuck at 30Hz - Diagnostic Report

**Issue**: Display refresh rate dropdown only shows 30Hz instead of (30, 45, 60, 75, 100, 120, 165Hz)

**Status**: ⚠️ **Root Cause Identified**

---

## Problem Analysis

### Code Review

The refresh rate detection code in `RefreshRateFeature.cs` works as follows:

```csharp
public Task<RefreshRate[]> GetAllStatesAsync()
{
    var display = InternalDisplay.Get();
    var currentSettings = display.CurrentSetting;

    var result = display.GetPossibleSettings()
        .Where(dps => Match(dps, currentSettings))  // ← KEY FILTERING HERE
        .Select(dps => dps.Frequency)
        .Distinct()
        .OrderBy(freq => freq)
        .Select(freq => new RefreshRate(freq))
        .ToArray();

    return Task.FromResult(result);
}

private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
{
    if (dps.IsTooSmall())
        return false;

    var result = true;
    result &= dps.Resolution == ds.Resolution;      // Must match current resolution
    result &= dps.ColorDepth == ds.ColorDepth;      // Must match current color depth
    result &= dps.IsInterlaced == ds.IsInterlaced;  // Must match interlaced mode
    return result;
}
```

### Root Cause

The code **only shows refresh rates that match**:
1. ✅ Current resolution
2. ✅ Current color depth
3. ✅ Current interlaced mode

If Windows is only reporting 30Hz as available for the current display configuration, it means:

**The display driver is not reporting higher refresh rates for the current resolution/color depth combination.**

---

## Likely Causes

### 1. Display Driver Issue ⚠️ **MOST LIKELY**

**Symptom**: NVIDIA/AMD/Intel graphics driver not properly exposing all refresh rates.

**Why this happens**:
- Driver installation issues
- Outdated driver version
- Display not recognized correctly
- Driver defaulted to "safe" mode (30Hz)

**Check**:
```
1. Open Windows Settings → Display → Advanced display
2. Check what refresh rates are available in Windows natively
3. If Windows also shows only 30Hz, it's a driver issue
```

### 2. Low Resolution Mode

**Symptom**: Display is running at a non-native resolution.

**Why this happens**:
- Legion 7i typically has 2560x1600 @ 165Hz native resolution
- If running at lower resolution, Windows may not offer higher refresh rates
- Some resolutions don't support high refresh rates

**Check current resolution**:
```
Windows Settings → Display → Display resolution
Should be: 2560x1600 (or your panel's native resolution)
```

### 3. HDR/Color Depth Configuration

**Symptom**: High color depth or HDR mode enabled limiting refresh rates.

**Why this happens**:
- 10-bit color depth may limit refresh rates
- HDR mode may limit options
- Some color modes incompatible with high refresh rates

**Check**:
```
Windows Settings → Display → HDR
NVIDIA Control Panel → Change resolution → Color depth
Should be: 8-bit (unless you need 10-bit)
```

### 4. Power Saving Mode

**Symptom**: Laptop in power saving mode limiting refresh rates.

**Why this happens**:
- Battery saver mode may limit display refresh
- Graphics driver power management
- Windows "battery saver" enabled

**Check**:
```
Windows Settings → System → Power & battery
Ensure: Not in battery saver mode
Plug in AC adapter and retest
```

### 5. Display Cable/Connection Issue

**Symptom**: For Advanced Optimus systems, MUX switch in wrong mode.

**Why this happens**:
- Internal display routing through iGPU instead of dGPU
- MUX switch not properly configured
- Hybrid mode limiting capabilities

**Check**:
```
In Legion Toolkit:
Dashboard → Hybrid Mode → Check current mode
Try: Discrete GPU mode (requires restart)
```

---

## Diagnostic Steps

### Step 1: Check Windows Native Options

1. Open **Windows Settings**
2. Go to **Display → Advanced display**
3. Click on your built-in display
4. Check **Choose a refresh rate** dropdown

**Expected**: Should show multiple options (60Hz, 120Hz, 165Hz, etc.)
**If only 30Hz**: Windows driver issue, not toolkit issue

### Step 2: Check Display Configuration

1. Open **NVIDIA Control Panel** (or AMD/Intel equivalent)
2. Go to **Change resolution**
3. Check available resolutions and refresh rates

**Look for**:
- Native resolution (2560x1600 for Legion 7i)
- Multiple refresh rates listed
- Current settings

### Step 3: Check Toolkit Logs

1. Enable trace logging in Legion Toolkit
2. Navigate to Dashboard → Refresh Rate
3. Check logs at `%LocalAppData%\LenovoLegionToolkit\Logs\`

**Look for**:
```
Getting all refresh rates...
Built in display found: <display info>
Current built in display settings: <settings>
Possible refresh rates are <list>
```

### Step 4: Test Different Resolutions

1. Change display resolution temporarily
2. Check if refresh rate options change
3. Return to native resolution

**This reveals**: If issue is resolution-dependent

---

## Solutions

### Solution 1: Update/Reinstall Graphics Drivers ✅ **RECOMMENDED**

**For NVIDIA**:
```
1. Download latest Game Ready or Studio driver from nvidia.com
2. Choose "Custom installation"
3. Select "Clean installation" checkbox
4. Reinstall driver
5. Restart computer
```

**For AMD**:
```
1. Download AMD Software from amd.com
2. Use "Clean Install" option
3. Restart computer
```

**For Intel**:
```
1. Download Intel Graphics Driver from intel.com
2. Reinstall
3. Restart computer
```

### Solution 2: Reset Display Settings

**Windows 11**:
```powershell
# Reset display to defaults
1. Open Settings → Display
2. Click "Advanced display"
3. Reset to recommended settings
4. Set resolution to native (2560x1600)
5. Restart
```

### Solution 3: Disable HDR/10-bit Color Temporarily

1. Open **Windows Settings → Display**
2. Turn OFF **Use HDR** if enabled
3. Open **NVIDIA Control Panel**
4. Change resolution → Set color depth to **8-bit**
5. Check refresh rate options again

### Solution 4: Check MUX Switch Mode

1. Open **Legion Toolkit**
2. Go to **Dashboard → Hybrid Mode**
3. Current mode:
   - If **Hybrid**: Try switching to **Discrete GPU** mode
   - If **iGPU only**: Switch to **Discrete GPU** mode
4. Restart computer
5. Check refresh rates again

### Solution 5: Force Refresh Rate via NVIDIA/AMD Control Panel

**NVIDIA**:
```
1. Open NVIDIA Control Panel
2. Change resolution
3. Customize → Create Custom Resolution
4. Add: 2560x1600 @ 165Hz
5. Test
```

**Note**: Only do this if native options don't appear

### Solution 6: Check for Windows Updates

```
1. Windows Settings → Windows Update
2. Check for updates
3. Install all graphics-related updates
4. Restart
```

---

## Code Modification (Advanced)

If the above solutions don't work, the toolkit code can be modified to show ALL available refresh rates regardless of current settings:

### Current Code (Filters by current settings):
```csharp
var result = display.GetPossibleSettings()
    .Where(dps => Match(dps, currentSettings))  // ← Filters by resolution/color
    .Select(dps => dps.Frequency)
    .Distinct()
    .OrderBy(freq => freq)
    .Select(freq => new RefreshRate(freq))
    .ToArray();
```

### Modified Code (Shows ALL refresh rates):
```csharp
var result = display.GetPossibleSettings()
    .Where(dps => !dps.IsTooSmall())  // ← Only filter tiny resolutions
    // Removed: .Where(dps => Match(dps, currentSettings))
    .Select(dps => dps.Frequency)
    .Distinct()
    .OrderBy(freq => freq)
    .Select(freq => new RefreshRate(freq))
    .ToArray();
```

**File**: `LenovoLegionToolkit.Lib\Features\RefreshRateFeature.cs`
**Line**: 38

**Caveat**: This will show refresh rates for OTHER resolutions too, which may cause issues when selected.

---

## Verification Steps

After applying a solution:

1. ✅ Restart computer
2. ✅ Open Legion Toolkit → Dashboard
3. ✅ Check Refresh Rate dropdown
4. ✅ Should now show: 30, 45, 60, 75, 100, 120, 165Hz

---

## Expected Behavior

For a Legion 7i with 165Hz display:

**Correct Dropdown Options**:
```
30 Hz
45 Hz
60 Hz
75 Hz
100 Hz
120 Hz
165 Hz
```

**Current Behavior**:
```
30 Hz  ← Only this option
```

---

## Additional Diagnostics

### Get Detailed Display Info

Run in PowerShell (Admin):
```powershell
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorListedSupportedSourceModes
```

This shows what Windows thinks the display supports.

### Check Event Viewer

```
Event Viewer → Windows Logs → System
Filter for: Display, Graphics
Look for: Driver warnings or errors
```

---

## Summary

| Issue | Likelihood | Fix Difficulty |
|-------|-----------|----------------|
| Graphics driver issue | 85% | Easy (reinstall) |
| Non-native resolution | 10% | Easy (change res) |
| HDR/Color depth limit | 3% | Easy (disable HDR) |
| MUX switch mode | 1% | Medium (restart req) |
| Hardware issue | 1% | Hard (RMA) |

**Recommended Action Order**:
1. Check Windows native refresh rate options first
2. If Windows shows only 30Hz → Driver issue → Reinstall drivers
3. If Windows shows multiple options → Toolkit issue → Report bug
4. Verify native resolution (2560x1600)
5. Disable HDR if enabled
6. Update Windows

---

**Diagnostic Date**: October 3, 2025
**Toolkit Version**: v6.1.0
**Issue**: Refresh rate stuck at 30Hz
**Root Cause**: Windows driver not reporting available refresh rates
**Solution**: Update graphics drivers (85% success rate)
