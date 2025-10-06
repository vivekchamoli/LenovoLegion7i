using System;
using System.Runtime.InteropServices;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Kernel Driver Interface - Advanced WinRing0 Integration
/// Provides comprehensive kernel-mode hardware access
///
/// CAPABILITIES:
/// - MSR (Model-Specific Register) read/write
/// - PCI configuration space access
/// - I/O port access (EC communication)
/// - Physical memory access
/// - ACPI table access
///
/// REQUIREMENTS:
/// - WinRing0x64.sys kernel driver loaded
/// - Administrator privileges
/// - Secure Boot compatible (signed driver)
/// </summary>
public class KernelDriverInterface
{
    // WinRing0 Advanced Functions
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool InitializeOls();

    [DllImport("WinRing0x64.dll")]
    private static extern void DeinitializeOls();

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern uint GetDllStatus();

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern uint GetDllVersion(out uint major, out uint minor, out uint revision, out uint release);

    // MSR Access
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool Rdmsr(uint index, out uint eax, out uint edx);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool Wrmsr(uint index, uint eax, uint edx);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool RdmsrTx(uint index, out uint eax, out uint edx, IntPtr affinityMask);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool WrmsrTx(uint index, uint eax, uint edx, IntPtr affinityMask);

    // PCI Configuration Space Access
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern uint ReadPciConfigDword(uint pciAddress, uint regAddress);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern void WritePciConfigDword(uint pciAddress, uint regAddress, uint value);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool ReadPciConfigDwordEx(uint pciAddress, uint regAddress, out uint value);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool WritePciConfigDwordEx(uint pciAddress, uint regAddress, uint value);

    // I/O Port Access (for EC communication)
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern byte ReadIoPortByte(ushort port);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern void WriteIoPortByte(ushort port, byte value);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern ushort ReadIoPortWord(ushort port);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern void WriteIoPortWord(ushort port, ushort value);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern uint ReadIoPortDword(ushort port);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern void WriteIoPortDword(ushort port, uint value);

    // Physical Memory Access
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool ReadMemory(IntPtr address, IntPtr buffer, uint size, uint unitSize);

    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool WriteMemory(IntPtr address, IntPtr buffer, uint size, uint unitSize);

    // Temperature Sensor Access
    [DllImport("WinRing0x64.dll", SetLastError = true)]
    private static extern bool Hlt();

    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Initialize kernel driver interface
    /// </summary>
    public static bool Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
                return true;

            try
            {
                if (!InitializeOls())
                {
                    var status = GetDllStatus();
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"WinRing0 initialization failed: Status=0x{status:X}");
                    return false;
                }

                GetDllVersion(out uint major, out uint minor, out uint revision, out uint release);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WinRing0 initialized: v{major}.{minor}.{revision}.{release}");

                _initialized = true;
                return true;
            }
            catch (DllNotFoundException)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WinRing0x64.dll not found - kernel access unavailable");
                return false;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WinRing0 initialization exception", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Shutdown kernel driver interface
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                try
                {
                    DeinitializeOls();
                    _initialized = false;

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"WinRing0 shutdown complete");
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"WinRing0 shutdown exception", ex);
                }
            }
        }
    }

    /// <summary>
    /// Check if kernel driver is available
    /// </summary>
    public static bool IsAvailable => _initialized;

    // ==================== MSR Operations ====================

    /// <summary>
    /// Read MSR on current CPU core
    /// </summary>
    public static ulong ReadMsr(uint msr)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        if (Rdmsr(msr, out uint eax, out uint edx))
        {
            ulong value = ((ulong)edx << 32) | eax;
            return value;
        }

        throw new InvalidOperationException($"MSR read failed: 0x{msr:X}");
    }

    /// <summary>
    /// Write MSR on current CPU core
    /// </summary>
    public static void WriteMsr(uint msr, ulong value)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        uint eax = (uint)(value & 0xFFFFFFFF);
        uint edx = (uint)((value >> 32) & 0xFFFFFFFF);

        if (!Wrmsr(msr, eax, edx))
            throw new InvalidOperationException($"MSR write failed: 0x{msr:X}");
    }

    /// <summary>
    /// Read MSR on specific CPU core(s)
    /// </summary>
    public static ulong ReadMsrTx(uint msr, IntPtr affinityMask)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        if (RdmsrTx(msr, out uint eax, out uint edx, affinityMask))
        {
            ulong value = ((ulong)edx << 32) | eax;
            return value;
        }

        throw new InvalidOperationException($"MSR read (affinity) failed: 0x{msr:X}");
    }

    /// <summary>
    /// Write MSR on specific CPU core(s)
    /// </summary>
    public static void WriteMsrTx(uint msr, ulong value, IntPtr affinityMask)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        uint eax = (uint)(value & 0xFFFFFFFF);
        uint edx = (uint)((value >> 32) & 0xFFFFFFFF);

        if (!WrmsrTx(msr, eax, edx, affinityMask))
            throw new InvalidOperationException($"MSR write (affinity) failed: 0x{msr:X}");
    }

    // ==================== PCI Configuration Space ====================

    /// <summary>
    /// Read PCI configuration DWORD
    /// </summary>
    public static uint ReadPciConfig(uint bus, uint device, uint function, uint offset)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        // PCI address format: 0x80000000 | (bus << 16) | (device << 11) | (function << 8)
        uint pciAddress = 0x80000000u | (bus << 16) | (device << 11) | (function << 8);

        if (ReadPciConfigDwordEx(pciAddress, offset, out uint value))
            return value;

        throw new InvalidOperationException($"PCI read failed: {bus:X2}:{device:X2}.{function:X} offset 0x{offset:X}");
    }

    /// <summary>
    /// Write PCI configuration DWORD
    /// </summary>
    public static void WritePciConfig(uint bus, uint device, uint function, uint offset, uint value)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        uint pciAddress = 0x80000000u | (bus << 16) | (device << 11) | (function << 8);

        if (!WritePciConfigDwordEx(pciAddress, offset, value))
            throw new InvalidOperationException($"PCI write failed: {bus:X2}:{device:X2}.{function:X} offset 0x{offset:X}");
    }

    // ==================== I/O Port Access (EC Communication) ====================

    /// <summary>
    /// Read byte from I/O port (EC data/command port)
    /// </summary>
    public static byte ReadPort(ushort port)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        return ReadIoPortByte(port);
    }

    /// <summary>
    /// Write byte to I/O port (EC data/command port)
    /// </summary>
    public static void WritePort(ushort port, byte value)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        WriteIoPortByte(port, value);
    }

    /// <summary>
    /// Read word from I/O port
    /// </summary>
    public static ushort ReadPortWord(ushort port)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        return ReadIoPortWord(port);
    }

    /// <summary>
    /// Write word to I/O port
    /// </summary>
    public static void WritePortWord(ushort port, ushort value)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        WriteIoPortWord(port, value);
    }

    // ==================== Physical Memory Access ====================

    /// <summary>
    /// Read physical memory (ACPI tables, firmware data)
    /// </summary>
    public static byte[] ReadPhysicalMemory(ulong address, uint size)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        byte[] buffer = new byte[size];
        IntPtr bufferPtr = Marshal.AllocHGlobal((int)size);

        try
        {
            if (ReadMemory(new IntPtr((long)address), bufferPtr, size, 1))
            {
                Marshal.Copy(bufferPtr, buffer, 0, (int)size);
                return buffer;
            }

            throw new InvalidOperationException($"Physical memory read failed: 0x{address:X}");
        }
        finally
        {
            Marshal.FreeHGlobal(bufferPtr);
        }
    }

    /// <summary>
    /// Write physical memory (dangerous - use with caution)
    /// </summary>
    public static void WritePhysicalMemory(ulong address, byte[] data)
    {
        if (!_initialized)
            throw new InvalidOperationException("Kernel driver not initialized");

        IntPtr bufferPtr = Marshal.AllocHGlobal(data.Length);

        try
        {
            Marshal.Copy(data, 0, bufferPtr, data.Length);

            if (!WriteMemory(new IntPtr((long)address), bufferPtr, (uint)data.Length, 1))
                throw new InvalidOperationException($"Physical memory write failed: 0x{address:X}");
        }
        finally
        {
            Marshal.FreeHGlobal(bufferPtr);
        }
    }
}

/// <summary>
/// Kernel driver status codes
/// </summary>
public enum KernelDriverStatus
{
    NoError = 0,
    DllNotFound = 1,
    DriverNotLoaded = 2,
    DriverNotFoundSys = 3,
    DriverUnloaded = 4,
    DriverNotLoadedSys = 5,
    DriverUnloadedSys = 6,
    HandleInvalid = 7,
    InvalidParameter = 8,
    UnknownError = 9
}
