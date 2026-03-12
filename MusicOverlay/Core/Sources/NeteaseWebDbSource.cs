using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MusicOverlay.Core.Models;
using Newtonsoft.Json.Linq;

namespace MusicOverlay.Core.Sources;

/// <summary>
/// Media source for NetEase Cloud Music using webdb.dat (historyTracks).
/// Title/Artist are parsed from the window title by regex.
/// Cover image is downloaded via album picUrl from webdb.dat.
/// </summary>
public class NeteaseWebDbSource : IMediaSource
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly string _processName;
    private readonly Regex? _titleRegex;
    private readonly string _webDbPath;
    private readonly int _pollIntervalMs;
    private static readonly HttpClient Http = new();

    private CancellationTokenSource? _cts;
    private MediaInfo _lastInfo = new();
    private string _lastWebDbCoverUrl = string.Empty;
    private string _lastWebDbCoverBase64 = string.Empty;
    private string _lastWebDbAlbumName = string.Empty;

    public string SourceId { get; }
    public bool IsRunning { get; private set; }
    public event EventHandler<MediaInfo>? MediaChanged;

    public NeteaseWebDbSource(
        string sourceId,
        string processName,
        string titleRegexPattern,
        string webDbPath,
        int pollIntervalMs = 2000)
    {
        SourceId = sourceId;
        _processName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        _webDbPath = Environment.ExpandEnvironmentVariables(webDbPath);
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
        var hwnd = FindBestWindowHandle(proc.Id, _titleRegex);
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return info;

        // --- Parse title / artist / album from window title ---
        var windowTitle = GetWindowTitle(hwnd);
        if (_titleRegex != null && !string.IsNullOrWhiteSpace(windowTitle))
        {
            var m = _titleRegex.Match(windowTitle);
            if (m.Success)
            {
                if (m.Groups["title"].Success)
                    info.Title = m.Groups["title"].Value.Trim();
                if (m.Groups["artist"].Success)
                    info.Artist = m.Groups["artist"].Value.Trim();
                if (m.Groups["album"].Success)
                    info.Album = m.Groups["album"].Value.Trim();
                info.IsPlaying = true;
            }
        }

        // --- Cover image + album fallback from WebDB ---
        info.CoverBase64 = GetCoverFromWebDb(out var albumName);
        if (string.IsNullOrWhiteSpace(info.Album) && !string.IsNullOrWhiteSpace(albumName))
            info.Album = albumName;

        return info;
    }

    private static IntPtr FindBestWindowHandle(int processId, Regex? titleRegex)
    {
        IntPtr bestMatch = IntPtr.Zero;
        IntPtr anyVisible = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != processId) return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (titleRegex != null && titleRegex.IsMatch(title))
            {
                bestMatch = hWnd;
                return false; // stop
            }

            if (anyVisible == IntPtr.Zero)
                anyVisible = hWnd;

            return true;
        }, IntPtr.Zero);

        return bestMatch != IntPtr.Zero ? bestMatch : anyVisible;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        var len = GetWindowText(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Reads the latest played track from NetEase CloudMusic webdb.dat (historyTracks)
    /// and downloads the album cover via picUrl.
    /// </summary>
    private string GetCoverFromWebDb(out string albumName)
    {
        albumName = string.Empty;
        try
        {
            var path = _webDbPath;
            if (string.IsNullOrWhiteSpace(path))
                path = @"%LocalAppData%\NetEase\CloudMusic\Library\webdb.dat";

            path = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(path)) return string.Empty;

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT playtime, jsonStr FROM historyTracks ORDER BY playtime DESC LIMIT 5";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var jsonStr = reader.GetString(1);
                var album = ExtractAlbumName(jsonStr);
                if (!string.IsNullOrWhiteSpace(album))
                    albumName = album;
                var url = ExtractCoverUrl(jsonStr);
                if (string.IsNullOrWhiteSpace(url)) continue;

                if (url == _lastWebDbCoverUrl && _lastWebDbCoverBase64.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(albumName))
                        albumName = _lastWebDbAlbumName;
                    return _lastWebDbCoverBase64;
                }

                var finalUrl = EnsureCoverParam(url);
                var bytes = Http.GetByteArrayAsync(finalUrl).GetAwaiter().GetResult();
                var base64 = Convert.ToBase64String(bytes);
                _lastWebDbCoverUrl = url;
                _lastWebDbCoverBase64 = base64;
                if (!string.IsNullOrWhiteSpace(albumName))
                    _lastWebDbAlbumName = albumName;
                return base64;
            }
        }
        catch
        {
            return string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractCoverUrl(string jsonStr)
    {
        try
        {
            var obj = JObject.Parse(jsonStr);
            var url =
                obj.SelectToken("album.picUrl")?.ToString() ??
                obj.SelectToken("al.picUrl")?.ToString() ??
                obj.SelectToken("picUrl")?.ToString();
            return url ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractAlbumName(string jsonStr)
    {
        try
        {
            var obj = JObject.Parse(jsonStr);
            var name =
                obj.SelectToken("album.name")?.ToString() ??
                obj.SelectToken("al.name")?.ToString();
            return name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string EnsureCoverParam(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (url.Contains("param=", StringComparison.OrdinalIgnoreCase)) return url;
        var sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}param=600y600";
    }

    public void Dispose() => Stop();
}
