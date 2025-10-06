using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Content Framerate Detector - Detects actual framerate of playing media
/// Used for intelligent refresh rate optimization (e.g., 24fps → 48Hz, 30fps → 60Hz)
/// </summary>
public class ContentFramerateDetector
{
    // Known media player processes
    private static readonly HashSet<string> MediaPlayerProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpc-hc", "mpc-be", "mpc-hc64", "mpc-be64", "potplayer", "potplayermini",
        "potplayer64", "kmplayer", "mpv", "mpv.net", "smplayer",
        "netflix", "disney", "disneyplus", "primevideo", "amazon", "hulu", "plex",
        "plexamp", "kodi", "youtube", "youtubemusic", "spotify", "spotifymusic"
    };

    // Known streaming app windows
    private static readonly HashSet<string> StreamingAppPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Netflix", "Disney+", "Disney Plus", "Prime Video", "Amazon Prime",
        "Hulu", "Plex", "YouTube", "Twitch", "Spotify"
    };

    /// <summary>
    /// Detect framerate from currently playing media
    /// Returns 0 if no media detected
    /// </summary>
    public Task<int> DetectFramerateAsync()
    {
        try
        {
            var processes = Process.GetProcesses();

            foreach (var proc in processes)
            {
                try
                {
                    // Check if it's a known media player
                    if (IsMediaPlayer(proc.ProcessName))
                    {
                        // Try to extract framerate from window title
                        var fps = ExtractFramerateFromTitle(proc.MainWindowTitle);
                        if (fps > 0)
                        {
                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Detected {fps}fps from {proc.ProcessName}");
                            return Task.FromResult(fps);
                        }

                        // Default assumption for media players
                        return Task.FromResult(30); // Most online content is 30fps
                    }

                    // Check for streaming services in browser tabs
                    if (IsBrowser(proc.ProcessName))
                    {
                        var title = proc.MainWindowTitle;
                        if (IsStreamingService(title))
                        {
                            var fps = ExtractFramerateFromTitle(title);
                            if (fps > 0)
                                return Task.FromResult(fps);

                            // Default for streaming services
                            return Task.FromResult(30); // Most streaming is 30fps
                        }
                    }
                }
                catch
                {
                    // Ignore per-process errors, continue checking others
                }
            }

            // No media detected
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to detect content framerate", ex);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Determine optimal refresh rate for detected content framerate
    /// </summary>
    public int GetOptimalRefreshRateForContent(int contentFPS, int[] availableRates)
    {
        if (contentFPS == 0 || availableRates.Length == 0)
            return 0;

        // Map content FPS to optimal refresh rate
        var targetHz = contentFPS switch
        {
            24 => 48,   // Movies: 24fps → 48Hz (perfect 2:1 cadence, no judder)
            25 => 50,   // PAL content: 25fps → 50Hz
            30 => 60,   // Streaming/YouTube: 30fps → 60Hz (2:1 cadence)
            48 => 48,   // High framerate movies
            50 => 50,   // PAL high framerate
            60 => 60,   // Standard gaming/video
            120 => 120, // High refresh content
            _ => 60     // Default to 60Hz for unknown content
        };

        // Find closest available refresh rate
        return FindClosestRefreshRate(targetHz, availableRates);
    }

    /// <summary>
    /// Find closest available refresh rate to target
    /// </summary>
    private int FindClosestRefreshRate(int target, int[] available)
    {
        if (available.Length == 0)
            return 0;

        // Try exact match first
        if (available.Contains(target))
            return target;

        // Find closest match
        var closest = available
            .OrderBy(rate => Math.Abs(rate - target))
            .First();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Target {target}Hz not available, using closest: {closest}Hz");

        return closest;
    }

    /// <summary>
    /// Check if process is a known media player
    /// </summary>
    private bool IsMediaPlayer(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        return MediaPlayerProcesses.Contains(processName);
    }

    /// <summary>
    /// Check if process is a web browser
    /// </summary>
    private bool IsBrowser(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var browsers = new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
        return browsers.Any(b => processName.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Check if window title indicates streaming service
    /// </summary>
    private bool IsStreamingService(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return StreamingAppPatterns.Any(pattern =>
            title.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extract framerate from window title
    /// Many players show "[24fps]" or "24p" in title
    /// </summary>
    private int ExtractFramerateFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 0;

        try
        {
            // Pattern 1: "[24fps]", "[30FPS]", etc.
            var fpsMatch = Regex.Match(title, @"\[?(\d{2,3})\s*fps\]?", RegexOptions.IgnoreCase);
            if (fpsMatch.Success && int.TryParse(fpsMatch.Groups[1].Value, out var fps1))
            {
                return fps1;
            }

            // Pattern 2: "24p", "30p", "60p", etc.
            var pMatch = Regex.Match(title, @"(\d{2,3})p\b", RegexOptions.IgnoreCase);
            if (pMatch.Success && int.TryParse(pMatch.Groups[1].Value, out var fps2))
            {
                return fps2;
            }

            // Pattern 3: Common movie indicators
            if (title.Contains("23.976", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("23.98", StringComparison.OrdinalIgnoreCase))
            {
                return 24; // 23.976fps = cinema framerate, round to 24
            }

            // Pattern 4: "@ 60Hz", "at 120Hz" (some players show refresh rate)
            var hzMatch = Regex.Match(title, @"[@at]\s*(\d{2,3})\s*hz", RegexOptions.IgnoreCase);
            if (hzMatch.Success && int.TryParse(hzMatch.Groups[1].Value, out var fps3))
            {
                return fps3;
            }
        }
        catch
        {
            // Ignore regex errors
        }

        return 0;
    }
}
