using System;

namespace LenovoLegionToolkit.Lib.PackageDownloader;

/// <summary>
/// Exception thrown when device serial number doesn't match Lenovo official support site
/// </summary>
public class SerialNumberMismatchException : Exception
{
    public string DeviceSerialNumber { get; }
    public string WebsiteSerialNumber { get; }

    public SerialNumberMismatchException(string deviceSerialNumber, string websiteSerialNumber)
        : base($"Device serial number '{deviceSerialNumber}' does not match website serial number '{websiteSerialNumber}'. This ensures downloads are validated for your specific device.")
    {
        DeviceSerialNumber = deviceSerialNumber;
        WebsiteSerialNumber = websiteSerialNumber;
    }

    public SerialNumberMismatchException(string message) : base(message)
    {
        DeviceSerialNumber = string.Empty;
        WebsiteSerialNumber = string.Empty;
    }

    public SerialNumberMismatchException(string message, Exception innerException) : base(message, innerException)
    {
        DeviceSerialNumber = string.Empty;
        WebsiteSerialNumber = string.Empty;
    }
}
