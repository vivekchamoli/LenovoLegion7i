# Native Dependencies

## WinRing0x64.dll + WinRing0x64.sys

**Source:** https://github.com/GermanAizek/WinRing0
**Version:** 1.3.1.19 (upgraded from 1.2.0)
**Files:**
  - WinRing0x64.dll (57 KB) - User-mode library
  - WinRing0x64.sys (15 KB) - Kernel driver (bundled)
**License:** BSD-style open source (modified from OpenLibSys)
**Purpose:** Kernel-mode hardware access for elite power management features

---

### What it does:
- MSR (Model-Specific Register) read/write access for CPU power control
- PCI configuration space access for device power management
- I/O port access (alternative to inpoutx64.dll)
- Physical memory access for ACPI tables
- Embedded Controller (EC) access

---

### How it works:
1. **User-Mode Application** → Calls WinRing0 API functions
2. **WinRing0x64.dll** → Loads kernel driver (WinRing0x64.sys)
3. **Kernel Driver** → Executes privileged hardware operations
4. **Hardware** → MSR registers, PCI config space, EC registers

**Elite Features Enabled:**
- CPU Power Limit Control (PL1/PL2) via MSR 0x610
- C-State Management for 5-15W idle savings
- Turbo Boost hardware control
- Real-time RAPL energy monitoring
- Advanced fan control via EC access

---

### License Terms:

From the OpenLibSys/WinRing0 project:

> "Copyright (c) 2007-2009 OpenLibSys.org. All rights reserved.
>
> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
>
> THIS SOFTWARE IS PROVIDED BY THE AUTHOR 'AS IS' AND ANY EXPRESS OR IMPLIED WARRANTIES ARE DISCLAIMED."

**Redistribution:** ✅ Permitted
**Commercial Use:** ✅ Permitted
**Attribution:** Required (see above)
**Warranty:** None provided

---

### Safety Notes:

⚠️ **CRITICAL: Kernel-mode driver with unrestricted hardware access**

- Requires **Administrator privileges** to load kernel driver
- Can read/write **any CPU register** (MSR access is unrestricted)
- Can access **all PCI devices** on the system
- Can access **physical memory** and firmware tables
- Legion Toolkit includes safety limits:
  - MSR access limited to power/thermal registers
  - Validates all MSR write values before execution
  - Read-only access for most registers
  - Thermal protection: Hardware limits always respected
  - No BIOS/firmware modification (read-only)

**Security Best Practices:**
- Driver auto-loads only when InitializeOls() is called
- Driver signature should be verified (production builds)
- Consider custom kernel driver for production (whitelisted operations only)
- Fallback to WMI/ACPI when WinRing0 unavailable

---

### Installation (Automatic):

**First Run:**
1. Application detects WinRing0x64.dll in same folder ✅
2. InitializeOls() loads embedded kernel driver (WinRing0x64.sys)
3. Windows may prompt for driver installation (on first load)
4. Click "Yes/Allow" to install driver
5. Driver loads automatically on subsequent runs ✅

**Driver Persistence:**
- Driver loads on-demand (not as Windows service)
- Unloads when application exits
- No system modification (no permanent installation)

**Manual Driver Management** (if automatic fails):
```batch
# Install driver (admin required)
sc create WinRing0_1_2_0 type= kernel binPath= "C:\Path\To\WinRing0x64.sys"
sc start WinRing0_1_2_0

# Remove driver
sc stop WinRing0_1_2_0
sc delete WinRing0_1_2_0
```

---

### Alternative (No DLL):

If WinRing0x64.dll is not present or driver installation fails:

- ✅ **Process Priority Optimization** still works (no kernel access needed)
- ✅ **Windows Power Profiles** still work (standard API)
- ✅ **WMI Power Control** still works (limited precision)
- ❌ **MSR Power Limits** unavailable (requires kernel access)
- ❌ **C-State Control** unavailable (requires MSR access)
- ❌ **Hardware Turbo Control** unavailable (requires MSR access)
- ❌ **EC Fan Control** unavailable (alternative: use inpoutx64.dll)

Application gracefully degrades to WMI/ACPI-based control.

---

### Troubleshooting:

**DLL Not Found:**
```
System.DllNotFoundException: Unable to load DLL 'WinRing0x64.dll'
```
- Check that WinRing0x64.dll is in same folder as application
- Verify DLL is not blocked by Windows (Right-click → Properties → Unblock)
- Check build output directory includes Native\WinRing0x64.dll

**Driver Load Failed:**
```
WinRing0 initialization failed: Status=0x2
```
- Run application as Administrator
- Check antivirus/security software isn't blocking driver
- Windows 11 Secure Boot may require signed drivers (see KERNEL_DRIVER_REQUIREMENTS.md)
- Enable Test Signing for development: `bcdedit /set testsigning on` (requires reboot)

**Access Denied:**
```
System.UnauthorizedAccessException: Access is denied
```
- MSR access requires Administrator privileges
- Run application as Administrator
- Check that kernel driver is loaded: `sc query WinRing0_1_2_0`

**Windows 11 Secure Boot Issues:**
- WinRing0 is unsigned - may be blocked by Memory Integrity (HVCI)
- Options:
  1. Disable Secure Boot in BIOS (for development)
  2. Disable Memory Integrity in Windows Security settings
  3. Use signed alternative (LibreHardwareMonitor driver)
  4. Production: Sign driver with EV certificate + WHQL

---

### Verification:

**Check if DLL is loaded:**
1. Run application with trace logging: `run_with_trace_logging.bat`
2. Trigger elite power feature
3. Look for these log messages:

**Success:**
```
[MSRAccess.cs#77] WinRing0 driver initialized successfully
[MSRAccess.cs#123] MSR access available - elite power control enabled
[EliteFeaturesManager.cs#72] MSR access initialized: Available
```

**Failure:**
```
[MSRAccess.cs#86] WinRing0x64.dll not found - MSR access unavailable
[EliteFeaturesManager.cs#72] MSR access initialized: Unavailable
[HardwareAbstractionLayer.cs#437] MSR Access: NO
```

---

### Technical Details:

**Function Exports:**
- `InitializeOls()` - Load kernel driver and initialize
- `DeinitializeOls()` - Unload kernel driver
- `Rdmsr(index, &eax, &edx)` - Read MSR register
- `Wrmsr(index, eax, edx)` - Write MSR register
- `ReadIoPortByte(port)` - Read I/O port (EC communication)
- `WriteIoPortByte(port, value)` - Write I/O port
- `ReadPciConfigDword(address, offset)` - Read PCI config space
- `WritePciConfigDword(address, offset, value)` - Write PCI config

**MSR Communication Example:**
```csharp
// Initialize driver
InitializeOls();

// Read CPU base frequency (safe, read-only register)
Rdmsr(0xCE, out uint eax, out uint edx); // MSR_PLATFORM_INFO
ulong platformInfo = ((ulong)edx << 32) | eax;
var ratio = (platformInfo >> 8) & 0xFF; // Bits 15:8
var baseFreqMHz = ratio * 100;

// Write CPU power limit (requires validation!)
ulong pl1 = 35; // 35W
ulong pl2 = 80; // 80W burst
ulong powerLimit = (pl1 & 0x7FFF) | (1UL << 15) | ((pl2 & 0x7FFF) << 32) | (1UL << 47);
Wrmsr(0x610, (uint)(powerLimit & 0xFFFFFFFF), (uint)(powerLimit >> 32));
```

**Used by:**
- `MSRAccess.cs` - CPU power control via MSR
- `KernelDriverInterface.cs` - Unified kernel driver interface
- `EmbeddedControllerAccess.cs` - EC fan control (alternative to inpoutx64.dll)
- `PCIePowerManager.cs` - PCIe device power management
- `HardwareAbstractionLayer.cs` - Hardware abstraction layer

---

### Version History:

**v1.3.1.19 (Latest - Bundled):**
- Windows 11 compatible (test signing required)
- x64 support
- Improved stability
- MSR access on multi-core CPUs
- ✅ Kernel driver (.sys) now bundled with application

**v1.2.0:**
- Initial documented version
- Windows 11 compatible (test signing required)
- x64 support

**v1.0.0 (Classic):**
- Original OpenLibSys release
- Windows XP-10 compatible

---

### Links:

- **GitHub (Fork):** https://github.com/GermanAizek/WinRing0
- **Original:** http://openlibsys.org/
- **Documentation:** See KERNEL_DRIVER_REQUIREMENTS.md
- **Alternative:** LibreHardwareMonitor driver (modern, signed)

---

**Bundled:** 2025-10-11 (v1.3.1.19 - KERNEL DRIVER INCLUDED ✅)
**Legion Toolkit Version:** v6.3.6+
**Tested On:** Windows 10/11 x64, Legion 7 16IRX9
**Status:** Development/Testing ⚠️ (Production: Use signed driver)
**Critical Fix:** WinRing0x64.sys kernel driver now bundled - Status=0x3 error resolved

---

## inpoutx64.dll

**Source:** http://www.highrez.co.uk/downloads/inpout32/
**Version:** 1.5.0.1
**File Size:** 96 KB
**License:** Freeware for personal and commercial use
**Purpose:** Direct I/O port access for EC (Embedded Controller) communication (Alternative to WinRing0)

---

### What it does:
- Provides InpOut32/InpOutx64 functions for direct port I/O access
- Enables communication with Legion 7i Embedded Controller (EC)
- Required for granular fan speed control (0-255 levels)
- Allows real-time hardware monitoring and control

---

### How it works:
1. **User-Mode Application** → Calls InpOut32/InpOutx64 functions
2. **inpoutx64.dll** → Bridges to kernel driver
3. **Kernel Driver** → Executes IN/OUT port instructions
4. **Hardware EC** → Receives commands and updates fan speeds

**EC Register Examples:**
- `0xB2` - CPU fan speed (0-255)
- `0xB3` - GPU fan speed (0-255)
- `0x66/0x62` - EC command/data ports

---

### License Terms:

From the official HighRez Software website:

> "This software is freeware and may be used for both personal and commercial purposes.
>
> The software is provided 'as-is' without warranty of any kind, either expressed or implied, including, but not limited to, the implied warranties of merchantability and fitness for a particular purpose. In no event shall HighRez be liable for any special, incidental, indirect or consequential damages whatsoever."

**Redistribution:** ✅ Permitted
**Commercial Use:** ✅ Permitted
**Attribution:** Appreciated but not required
**Warranty:** None provided

---

### Safety Notes:

⚠️ **WARNING: Direct hardware access requires caution**

- Requires **Administrator privileges** to install kernel driver
- Only use for intended purpose (EC communication for fan control)
- Incorrect port I/O can damage hardware
- Legion Toolkit includes safety limits to prevent misuse:
  - Min fan speed: 0 (automatic)
  - Max fan speed: 255 (100%)
  - Thermal protection: EC has built-in safety limits
  - Validation: All values checked before being sent to EC

---

### Installation (Automatic):

**First Run:**
1. Application detects inpoutx64.dll in same folder ✅
2. Windows may prompt to install kernel driver
3. Click "Yes/Allow" to install driver
4. Driver persists across reboots ✅

**Manual Driver Installation** (if automatic fails):
1. Download inpout32 package from official site
2. Run `InstallDriver.exe` as Administrator
3. Reboot computer
4. Driver is now installed permanently

---

### Alternative (No DLL):

If inpoutx64.dll is not present or driver installation fails:

- ✅ **Fan Profiles** still work (Quiet/Balanced/Performance via WMI)
- ✅ **Thermal Optimization** still works
- ✅ **Acoustic Optimization** still works
- ❌ **Direct fan speed control** (0-255) unavailable
- ❌ **Real-time EC monitoring** unavailable

Application gracefully degrades to WMI-based control.

---

### Troubleshooting:

**DLL Not Found:**
```
System.DllNotFoundException: Unable to load DLL 'inpoutx64.dll'
```
- Check that inpoutx64.dll is in same folder as application
- Verify DLL is not blocked by Windows (Right-click → Properties → Unblock)

**Access Denied:**
```
System.UnauthorizedAccessException: Access is denied
```
- Run application as Administrator
- Install kernel driver (InstallDriver.exe)
- Reboot after driver installation

**Driver Installation Failed:**
- Check antivirus/security software isn't blocking installation
- Manually download inpout32 package and install driver
- Check Windows Event Viewer for driver errors

---

### Verification:

**Check if DLL is loaded:**
1. Run application with trace logging: `run_with_trace_logging.bat`
2. Trigger fan control feature
3. Look for these log messages:

**Success:**
```
[Gen9ECController.cs#666] Writing EC register 0xB2 = 128 (CPU fan)
[Gen9ECController.cs#666] Writing EC register 0xB3 = 128 (GPU fan)
[ThermalOptimizer.cs#376] Applied Quiet fan profile successfully
```

**Failure:**
```
System.DllNotFoundException: Unable to load DLL 'inpoutx64.dll'
[ThermalOptimizer.cs#401] Failed to apply Quiet fan profile
```

---

### Technical Details:

**Function Exports:**
- `Inp32(portAddress)` - Read byte from I/O port
- `Out32(portAddress, data)` - Write byte to I/O port
- `DlPortReadPortUchar(port)` - Read port (kernel call)
- `DlPortWritePortUchar(port, value)` - Write port (kernel call)

**EC Communication Protocol:**
```csharp
// Wait for EC to be ready
while ((InB(0x66) & 0x02) != 0) { }

// Write command
OutB(0x66, 0x81); // Write to RAM command

// Wait for EC
while ((InB(0x66) & 0x02) != 0) { }

// Write register address
OutB(0x62, 0xB2); // CPU fan register

// Write value
OutB(0x62, fanSpeed); // 0-255
```

**Used by:**
- `Gen9ECController.cs` - EC communication layer
- `ThermalOptimizer.cs` - Thermal management
- `AcousticOptimizer.cs` - Acoustic optimization

---

### Version History:

**v1.5.0.1 (Sep 26, 2023):**
- Latest stable version
- Windows 11 compatible
- x64 support
- Code signing (trusted)

---

### Links:

- **Official Website:** http://www.highrez.co.uk/downloads/inpout32/
- **Documentation:** http://www.highrez.co.uk/downloads/inpout32/default.htm
- **Source Code:** Available on official site (optional purchase)

---

**Bundled:** 2025-01-06
**Legion Toolkit Version:** v6.4.3+
**Tested On:** Windows 10/11 x64, Legion 7i Gen 9
**Status:** Production Ready ✅
