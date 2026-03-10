using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MusicOverlay.Core.Models;

namespace MusicOverlay.Core.Sources;

/// <summary>
/// Media source for apps that don't support SMTC (e.g. NetEase Cloud Music).
/// 
/// Title/Artist: parsed from the window title using a user-configurable regex.
///   The regex must contain named groups: (?&lt;title&gt;...) and (?&lt;artist&gt;...)
///
/// Cover image: two strategies selectable via config:
///   "cache"      - monitors a local cache directory for the newest image file
///   "screenshot" - captures the app window with PrintWindow and crops a region
///
/// Both strategies and their parameters are fully configured in sources.json.
/// </summary>
public class WindowCaptureSource : IMediaSource
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly string _processName;
    private readonly Regex? _titleRegex;
    private readonly string _coverSource;       // "cache" or "screenshot"
    private readonly string _cachePath;
    private readonly CropRect _screenshotCrop;
    private readonly int _pollIntervalMs;

    private CancellationTokenSource? _cts;
    private MediaInfo _lastInfo = new();

    public string SourceId { get; }
    public bool IsRunning { get; private set; }
    public event EventHandler<MediaInfo>? MediaChanged;

    public WindowCaptureSource(
        string sourceId,
        string processName,
        string titleRegexPattern,
        string coverSource,
        string cachePath,
        CropRect screenshotCrop,
        int pollIntervalMs = 2000)
    {
        SourceId = sourceId;
        _processName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        _coverSource = coverSource;
        _cachePath = Environment.ExpandEnvironmentVariables(cachePath);
        _screenshotCrop = screenshotCrop;
        _pollIntervalMs = Math.Max(500, pollIntervalMs);

        if (!string.IsNullOrWhiteSpace(titleRegexPattern))
        {
            try { _titleRegex = new Regex(titleRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid regex — title parsing will be skipped */ }
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
    }

    public async Task<MediaInfo> GetCurrentAsync()
    {
        return await Task.Run(FetchCurrent);
    }

    // ── private ──────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var info = await GetCurrentAsync();
                if (!info.Equals(_lastInfo))
                {
                    _lastInfo = info;
                    MediaChanged?.Invoke(this, info);
                }
            }
            catch { }

            await Task.Delay(_pollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private MediaInfo FetchCurrent()
    {
        var info = new MediaInfo { SourceId = SourceId };

        // --- Find the target process window ---
        var procs = Process.GetProcessesByName(_processName);
        if (procs.Length == 0) return info;

        var proc = procs[0];
        var hwnd = proc.MainWindowHandle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return info;

        // --- Parse title / artist from window title ---
        var windowTitle = proc.MainWindowTitle;
        if (_titleRegex != null && !string.IsNullOrWhiteSpace(windowTitle))
        {
            var m = _titleRegex.Match(windowTitle);
            if (m.Success)
            {
                info.Title = m.Groups["title"].Value.Trim();
                info.Artist = m.Groups["artist"].Value.Trim();
                info.IsPlaying = true;
            }
        }

        // --- Cover image ---
        info.CoverBase64 = _coverSource == "cache"
            ? GetCoverFromCache()
            : GetCoverFromScreenshot(hwnd);

        return info;
    }

    /// <summary>
    /// Finds the newest image file in the configured cache directory.
    /// NetEase writes the current cover to its local cache; we grab the most-recently-modified one.
    /// The exact subfolder and filename pattern can be tuned in sources.json (cache_path).
    /// </summary>
    private string GetCoverFromCache()
    {
        if (string.IsNullOrWhiteSpace(_cachePath) || !Directory.Exists(_cachePath))
            return string.Empty;

        try
        {
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var newest = Directory
                .EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            if (newest == null) return string.Empty;
            var bytes = File.ReadAllBytes(newest);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Captures the app window using PrintWindow (works even when minimized / behind other windows),
    /// then crops the region specified in sources.json (screenshot_crop) as relative fractions.
    /// Adjust crop values in config to frame the album art precisely for your window size.
    /// </summary>
    private string GetCoverFromScreenshot(IntPtr hwnd)
    {
        try
        {
            GetWindowRect(hwnd, out var rect);
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return string.Empty;

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            IntPtr hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT = 2
            g.ReleaseHdc(hdc);

            // Crop using relative fractions from config
            int cx = (int)(w * _screenshotCrop.X);
            int cy = (int)(h * _screenshotCrop.Y);
            int cw = (int)(w * _screenshotCrop.Width);
            int ch = (int)(h * _screenshotCrop.Height);
            cw = Math.Max(1, Math.Min(cw, w - cx));
            ch = Math.Max(1, Math.Min(ch, h - cy));

            using var cropped = bmp.Clone(new Rectangle(cx, cy, cw, ch), PixelFormat.Format32bppArgb);
            using var ms = new MemoryStream();
            cropped.Save(ms, ImageFormat.Jpeg);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose() => Stop();
}

/// <summary>Relative crop rectangle (values 0.0 – 1.0).</summary>
public record CropRect(double X, double Y, double Width, double Height);
