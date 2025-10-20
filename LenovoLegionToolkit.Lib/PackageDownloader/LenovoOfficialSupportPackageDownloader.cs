using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.PackageDownloader;

/// <summary>
/// Package downloader that validates device serial number against Lenovo's official support site
/// before downloading packages. Ensures packages are validated for the specific device.
/// </summary>
public partial class LenovoOfficialSupportPackageDownloader(HttpClientFactory httpClientFactory, WarrantyChecker warrantyChecker)
    : AbstractPackageDownloader(httpClientFactory)
{
    private readonly WarrantyChecker _warrantyChecker = warrantyChecker;
    [GeneratedRegex(@"serialNumber[""']?\s*:\s*[""']([A-Z0-9]+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex SerialNumberPattern();

    [GeneratedRegex(@"machineType[""']?\s*:\s*[""']([A-Z0-9]+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MachineTypePattern();

    private const string CATALOG_BASE_URL = "https://pcsupport.lenovo.com/us/en/api/v4/downloads/drivers?productId=";
    private const string SUPPORT_PAGE_BASE_URL = "https://pcsupport.lenovo.com/in/en/products";

    public override async Task<List<Package>> GetPackagesAsync(string machineType, OS os, IProgress<float>? progress = null, CancellationToken token = default)
    {
        progress?.Report(-1); // Indeterminate progress during validation

        // Get device information including serial number
        var machineInfo = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Device S/N: {machineInfo.SerialNumber}, Model: {machineInfo.Model}, MachineType: {machineInfo.MachineType}");

        // Validate serial number against official support site
        await ValidateSerialNumberAsync(machineInfo, token).ConfigureAwait(false);

        // Once validated, fetch packages using the reliable PC Support API
        var osString = os switch
        {
            OS.Windows11 => "Windows 11",
            OS.Windows10 => "Windows 10",
            OS.Windows8 => "Windows 8",
            OS.Windows7 => "Windows 7",
            _ => throw new InvalidOperationException(nameof(os)),
        };

        using var httpClient = HttpClientFactory.Create();
        httpClient.DefaultRequestHeaders.Referrer = new Uri("https://pcsupport.lenovo.com/");

        progress?.Report(0);

        var catalogJson = await httpClient.GetStringAsync($"{CATALOG_BASE_URL}{machineType}", token).ConfigureAwait(false);
        var catalogJsonNode = JsonNode.Parse(catalogJson);
        var downloadsNode = catalogJsonNode?["body"]?["DownloadItems"]?.AsArray();

        if (downloadsNode is null)
            return [];

        var packages = new List<Package>();
        foreach (var downloadNode in downloadsNode)
        {
            if (!IsCompatible(downloadNode, osString))
                continue;

            var package = ParsePackage(downloadNode!);
            if (package is null)
                continue;

            packages.Add(package.Value);
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Retrieved {packages.Count} validated packages for S/N: {machineInfo.SerialNumber}");

        return packages;
    }

    /// <summary>
    /// Validates device serial number against Lenovo official support page
    /// </summary>
    private async Task ValidateSerialNumberAsync(MachineInformation machineInfo, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(machineInfo.SerialNumber))
            throw new SerialNumberMismatchException("Device serial number could not be retrieved. Ensure you're running on a genuine Lenovo device.");

        try
        {
            using var httpClient = HttpClientFactory.Create();
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://pcsupport.lenovo.com/");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            // Get FULL product path from warranty info (cached) or API
            // Format: LAPTOPS-AND-NETBOOKS/LEGION-SERIES/LEGION-7-16IRX9/83FD/83FDCTO1WW/MP2QVW7P
            var productPath = await GetProductIdAsync(httpClient, machineInfo, token).ConfigureAwait(false);

            // Construct official support URL with FULL product path
            // Format: /products/{FULL_PATH}
            var supportUrl = $"{SUPPORT_PAGE_BASE_URL}/{productPath}";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Validating device against official support page: {supportUrl}");

            // Fetch the support page
            var response = await httpClient.GetAsync(supportUrl, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Support page returned status: {response.StatusCode}. Product path may be invalid.");

                throw new SerialNumberMismatchException($"Unable to access official support page for this device (HTTP {response.StatusCode}). Device may not be supported.");
            }

            var pageContent = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            // If we successfully loaded the official support page, the device is valid
            // The product path itself already contains the full device information
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Successfully accessed official support page. Device validated.");

            // Optional: Extract and verify serial number and machine type if present
            var serialMatch = SerialNumberPattern().Match(pageContent);
            var machineTypeMatch = MachineTypePattern().Match(pageContent);

            // Verify machine type if found in page
            if (machineTypeMatch.Success)
            {
                var pageMachineType = machineTypeMatch.Groups[1].Value;
                if (!pageMachineType.Equals(machineInfo.MachineType, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Machine type mismatch. Expected: {machineInfo.MachineType}, Found: {pageMachineType}");

                    throw new SerialNumberMismatchException($"Machine type mismatch. Expected '{machineInfo.MachineType}' but found '{pageMachineType}' on official support page.");
                }
            }

            // Verify serial number if found in page
            if (serialMatch.Success)
            {
                var pageSerialNumber = serialMatch.Groups[1].Value;
                if (!pageSerialNumber.Equals(machineInfo.SerialNumber, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Serial number mismatch. Expected: {machineInfo.SerialNumber}, Found: {pageSerialNumber}");

                    throw new SerialNumberMismatchException(machineInfo.SerialNumber, pageSerialNumber);
                }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Device validation successful for S/N: {machineInfo.SerialNumber}");
        }
        catch (SerialNumberMismatchException)
        {
            throw; // Re-throw validation failures
        }
        catch (HttpRequestException ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HTTP error during serial number validation", ex);

            throw new SerialNumberMismatchException($"Unable to validate device serial number: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Error during serial number validation", ex);

            throw new SerialNumberMismatchException($"Serial number validation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieves full product path from warranty info (cached) or Lenovo API for constructing official support URL
    /// Returns: LAPTOPS-AND-NETBOOKS/LEGION-SERIES/LEGION-7-16IRX9/83FD/83FDCTO1WW/MP2QVW7P
    /// </summary>
    private async Task<string> GetProductIdAsync(HttpClient httpClient, MachineInformation machineInfo, CancellationToken token)
    {
        try
        {
            // First, try to get full product path from cached warranty info (much faster!)
            var warrantyInfo = await _warrantyChecker.GetWarrantyInfo(machineInfo, forceRefresh: false, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(warrantyInfo?.ProductId))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Using cached product path from warranty info: {warrantyInfo.Value.ProductId}");

                return warrantyInfo.Value.ProductId;
            }

            // Fallback: Fetch from Lenovo's product API (same as warranty checker)
            var productApiUrl = $"https://pcsupport.lenovo.com/dk/en/api/v4/mse/getproducts?productId={machineInfo.SerialNumber}";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Fetching product path from API: {productApiUrl}");

            var productString = await httpClient.GetStringAsync(productApiUrl, token).ConfigureAwait(false);
            var productNode = JsonNode.Parse(productString);
            var firstProductNode = (productNode as JsonArray)?.FirstOrDefault();
            var id = firstProductNode?["Id"];

            // Return the FULL path from API
            // Format: LAPTOPS-AND-NETBOOKS/LEGION-SERIES/LEGION-7-16IRX9/83FD/83FDCTO1WW/MP2QVW7P
            var productPath = id?.ToString();

            if (string.IsNullOrWhiteSpace(productPath))
            {
                throw new SerialNumberMismatchException($"Unable to retrieve product information for serial number: {machineInfo.SerialNumber}");
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Retrieved product path: {productPath}");

            return productPath;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to retrieve product path from API", ex);

            throw new SerialNumberMismatchException($"Unable to retrieve product information: {ex.Message}", ex);
        }
    }

    private static Package? ParsePackage(JsonNode downloadNode)
    {
        var id = downloadNode["ID"]!.ToJsonString();
        var category = downloadNode["Category"]!["Name"]!.ToString();
        var title = downloadNode["Title"]!.ToString();
        var description = downloadNode["Summary"]!.ToString();
        var version = downloadNode["SummaryInfo"]!["Version"]!.ToString();

        var filesNode = downloadNode["Files"]!.AsArray();
        var mainFileNode = filesNode.FirstOrDefault(n => n!["TypeString"]!.ToString().Equals("exe", StringComparison.InvariantCultureIgnoreCase))
                           ?? filesNode.FirstOrDefault(n => n!["TypeString"]!.ToString().Equals("zip", StringComparison.InvariantCultureIgnoreCase))
                           ?? filesNode.FirstOrDefault();

        if (mainFileNode is null)
            return null;

        var fileLocation = mainFileNode["URL"]!.ToString();
        var fileName = new Uri(fileLocation).Segments.LastOrDefault("file");
        var fileSize = mainFileNode["Size"]!.ToString();
        var fileCrc = mainFileNode["SHA256"]?.ToString();
        var releaseDateUnix = long.Parse(mainFileNode["Date"]!["Unix"]!.ToString());
        var releaseDate = DateTimeOffset.FromUnixTimeMilliseconds(releaseDateUnix).DateTime;

        var readmeFileNode = filesNode.FirstOrDefault(n => n!["TypeString"]!.ToString().Equals("txt readme", StringComparison.InvariantCultureIgnoreCase))
                              ?? filesNode.FirstOrDefault(n => n!["TypeString"]!.ToString().Equals("html", StringComparison.InvariantCultureIgnoreCase));

        var readme = readmeFileNode?["URL"]?.ToString();

        return new()
        {
            Id = id,
            Title = title,
            Description = title == description ? string.Empty : description,
            Version = version,
            Category = category,
            FileName = fileName,
            FileSize = fileSize,
            FileCrc = fileCrc,
            ReleaseDate = releaseDate,
            Readme = readme,
            FileLocation = fileLocation,
        };
    }

    private static bool IsCompatible(JsonNode? downloadNode, string osString)
    {
        var operatingSystems = downloadNode?["OperatingSystemKeys"]?.AsArray();

        if (operatingSystems is null || operatingSystems.IsEmpty())
            return true;

        foreach (var operatingSystem in operatingSystems)
            if (operatingSystem is not null && operatingSystem.ToString().StartsWith(osString, StringComparison.CurrentCultureIgnoreCase))
                return true;

        return false;
    }
}
