using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MusicOverlay.Core.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MusicOverlay.Core.Sources;

/// <summary>
/// Reads media info from Windows SMTC (GlobalSystemMediaTransportControls).
/// Supports optional filtering by process name (preferred_app in config).
/// Optionally applies regex patterns to the raw title/artist fields to extract
/// the correct values — useful when an app embeds artist info inside the title
/// or vice-versa.
/// </summary>
public class SmtcMediaSource : IMediaSource
{
    private readonly string _preferredApp;   // app user model filter, empty = auto (current session)
    private readonly string _windowProcessName;
    private readonly Regex? _windowTitleRegex;
    private readonly int _pollIntervalMs;

    // Regex applied to raw SMTC title field; may capture (?<title>), (?<artist>), (?<album>).
    private readonly Regex? _titleRegex;
    // Regex applied to raw SMTC artist field; may capture (?<artist>), (?<title>), (?<album>).
    private readonly Regex? _artistRegex;
    // Regex applied to raw SMTC album field; may capture (?<album>), (?<title>), (?<artist>).
    private readonly Regex? _albumRegex;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private CancellationTokenSource? _cts;
    private MediaInfo _lastInfo = new();

    public string SourceId { get; }
    public bool IsRunning { get; private set; }
    public event EventHandler<MediaInfo>? MediaChanged;

    public SmtcMediaSource(
        string sourceId,
        string preferredApp = "",
        string titleRegex = "",
        string artistRegex = "",
        string albumRegex = "",
        string windowTitleRegex = "",
        int pollIntervalMs = 1000)
    {
        SourceId = sourceId;
        _preferredApp = preferredApp.ToLowerInvariant().Trim();
        _windowProcessName = ResolveProcessName(preferredApp);
        _pollIntervalMs = Math.Max(500, pollIntervalMs);

        if (!string.IsNullOrWhiteSpace(titleRegex))
            try { _titleRegex = new Regex(titleRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid pattern — skip */ }

        if (!string.IsNullOrWhiteSpace(artistRegex))
            try { _artistRegex = new Regex(artistRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid pattern — skip */ }

        if (!string.IsNullOrWhiteSpace(albumRegex))
            try { _albumRegex = new Regex(albumRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid pattern — skip */ }

        if (!string.IsNullOrWhiteSpace(windowTitleRegex))
            try { _windowTitleRegex = new Regex(windowTitleRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid pattern — skip */ }
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
        try
        {
            var session = await GetTargetSessionAsync();
            if (session == null) return new MediaInfo { SourceId = SourceId };
            return await ReadSessionAsync(session);
        }
        catch
        {
            return new MediaInfo { SourceId = SourceId };
        }
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
            catch { /* swallow individual poll errors */ }

            await Task.Delay(_pollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> GetTargetSessionAsync()
    {
        _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        if (string.IsNullOrEmpty(_preferredApp))
        {
            // Use the OS-selected "current" session
            return _sessionManager.GetCurrentSession();
        }

        // Search all sessions for the preferred app by source app user model ID
        var sessions = _sessionManager.GetSessions();
        foreach (var s in sessions)
        {
            var appId = s.SourceAppUserModelId?.ToLowerInvariant() ?? "";
            if (appId.Contains(_preferredApp))
                return s;
        }
        return null;
    }

    private async Task<MediaInfo> ReadSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var props = await session.TryGetMediaPropertiesAsync();
        var pb = session.GetPlaybackInfo();

        var rawTitle  = props.Title  ?? string.Empty;
        var rawArtist = props.Artist ?? string.Empty;
        var rawAlbum  = props.AlbumTitle ?? string.Empty;

        // Apply title regex to the raw title field.
        string? titleFromTitleRegex  = null;
        string? artistFromTitleRegex = null;
        string? albumFromTitleRegex  = null;
        if (_titleRegex != null && rawTitle.Length > 0)
        {
            var m = _titleRegex.Match(rawTitle);
            if (m.Success)
            {
                if (m.Groups["title"].Success)  titleFromTitleRegex  = m.Groups["title"].Value.Trim();
                if (m.Groups["artist"].Success) artistFromTitleRegex = m.Groups["artist"].Value.Trim();
                if (m.Groups["album"].Success)  albumFromTitleRegex  = m.Groups["album"].Value.Trim();
            }
        }

        // Apply artist regex to the raw artist field.
        string? artistFromArtistRegex = null;
        string? titleFromArtistRegex  = null;
        string? albumFromArtistRegex  = null;
        if (_artistRegex != null && rawArtist.Length > 0)
        {
            var m = _artistRegex.Match(rawArtist);
            if (m.Success)
            {
                if (m.Groups["artist"].Success) artistFromArtistRegex = m.Groups["artist"].Value.Trim();
                if (m.Groups["title"].Success)  titleFromArtistRegex  = m.Groups["title"].Value.Trim();
                if (m.Groups["album"].Success)  albumFromArtistRegex  = m.Groups["album"].Value.Trim();
            }
        }

        // Apply album regex to the raw album field.
        string? albumFromAlbumRegex  = null;
        string? titleFromAlbumRegex  = null;
        string? artistFromAlbumRegex = null;
        if (_albumRegex != null && rawAlbum.Length > 0)
        {
            var m = _albumRegex.Match(rawAlbum);
            if (m.Success)
            {
                if (m.Groups["album"].Success)  albumFromAlbumRegex  = m.Groups["album"].Value.Trim();
                if (m.Groups["title"].Success)  titleFromAlbumRegex  = m.Groups["title"].Value.Trim();
                if (m.Groups["artist"].Success) artistFromAlbumRegex = m.Groups["artist"].Value.Trim();
            }
        }

        // Window title regex (optional, for SMTC apps that embed metadata in window title).
        string? titleFromWindowRegex  = null;
        string? artistFromWindowRegex = null;
        string? albumFromWindowRegex  = null;
        if (_windowTitleRegex != null && _windowProcessName.Length > 0)
        {
            var windowTitle = GetWindowTitleForProcess(_windowProcessName, _windowTitleRegex);
            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                var m = _windowTitleRegex.Match(windowTitle);
                if (m.Success)
                {
                    if (m.Groups["title"].Success)  titleFromWindowRegex  = m.Groups["title"].Value.Trim();
                    if (m.Groups["artist"].Success) artistFromWindowRegex = m.Groups["artist"].Value.Trim();
                    if (m.Groups["album"].Success)  albumFromWindowRegex  = m.Groups["album"].Value.Trim();
                }
            }
        }

        // Merge: the "natural" source takes priority for each field, then fallback to other regexes.
        // For title  → title_regex's title  > artist_regex's title  > album_regex's title  > window_regex's title  > raw title
        // For artist → artist_regex's artist > title_regex's artist > album_regex's artist > window_regex's artist > raw artist
        // For album  → album_regex's album  > title_regex's album  > artist_regex's album  > window_regex's album  > raw album
        var finalTitle  = titleFromTitleRegex  ?? titleFromArtistRegex  ?? titleFromAlbumRegex  ?? titleFromWindowRegex  ?? rawTitle;
        var finalArtist = artistFromArtistRegex ?? artistFromTitleRegex ?? artistFromAlbumRegex ?? artistFromWindowRegex ?? rawArtist;
        var finalAlbum  = albumFromAlbumRegex  ?? albumFromTitleRegex  ?? albumFromArtistRegex  ?? albumFromWindowRegex  ?? rawAlbum;

        var info = new MediaInfo
        {
            SourceId  = SourceId,
            Title     = finalTitle,
            Artist    = finalArtist,
            Album     = finalAlbum,
            IsPlaying = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
        };

        if (props.Thumbnail != null)
            info.CoverBase64 = await ReadThumbnailBase64Async(props.Thumbnail);

        return info;
    }

    private static async Task<string> ReadThumbnailBase64Async(IRandomAccessStreamReference thumbnailRef)
    {
        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose() => Stop();

    // ── window title helpers ────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static string ResolveProcessName(string preferredApp)
    {
        if (preferredApp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return preferredApp.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        return string.Empty;
    }

    private static string GetWindowTitleForProcess(string processName, Regex? titleRegex)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0) return string.Empty;

        var proc = procs[0];
        var hwnd = FindBestWindowHandle(proc.Id, titleRegex);
        if (hwnd == IntPtr.Zero) return string.Empty;

        return GetWindowTitle(hwnd);
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
                return false;
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
}
