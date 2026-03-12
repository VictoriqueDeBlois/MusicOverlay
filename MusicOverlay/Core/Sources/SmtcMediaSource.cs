using System.IO;
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
    private readonly string _preferredApp;   // process name filter, empty = auto (current session)
    private readonly int _pollIntervalMs;

    // Regex applied to raw SMTC title field; may capture (?<title>) and/or (?<artist>).
    private readonly Regex? _titleRegex;
    // Regex applied to raw SMTC artist field; may capture (?<artist>) and/or (?<title>).
    private readonly Regex? _artistRegex;

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
        int pollIntervalMs = 1000)
    {
        SourceId = sourceId;
        _preferredApp = preferredApp.ToLowerInvariant().Trim();
        _pollIntervalMs = Math.Max(500, pollIntervalMs);

        if (!string.IsNullOrWhiteSpace(titleRegex))
            try { _titleRegex = new Regex(titleRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { /* invalid pattern — skip */ }

        if (!string.IsNullOrWhiteSpace(artistRegex))
            try { _artistRegex = new Regex(artistRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
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

        // Apply title regex to the raw title field.
        string? titleFromTitleRegex  = null;
        string? artistFromTitleRegex = null;
        if (_titleRegex != null && rawTitle.Length > 0)
        {
            var m = _titleRegex.Match(rawTitle);
            if (m.Success)
            {
                if (m.Groups["title"].Success)  titleFromTitleRegex  = m.Groups["title"].Value.Trim();
                if (m.Groups["artist"].Success) artistFromTitleRegex = m.Groups["artist"].Value.Trim();
            }
        }

        // Apply artist regex to the raw artist field.
        string? artistFromArtistRegex = null;
        string? titleFromArtistRegex  = null;
        if (_artistRegex != null && rawArtist.Length > 0)
        {
            var m = _artistRegex.Match(rawArtist);
            if (m.Success)
            {
                if (m.Groups["artist"].Success) artistFromArtistRegex = m.Groups["artist"].Value.Trim();
                if (m.Groups["title"].Success)  titleFromArtistRegex  = m.Groups["title"].Value.Trim();
            }
        }

        // Merge: the "natural" source takes priority for each field.
        // For title  → title_regex's title  > artist_regex's title  > raw title
        // For artist → artist_regex's artist > title_regex's artist > raw artist
        var finalTitle  = titleFromTitleRegex  ?? titleFromArtistRegex  ?? rawTitle;
        var finalArtist = artistFromArtistRegex ?? artistFromTitleRegex ?? rawArtist;

        var info = new MediaInfo
        {
            SourceId  = SourceId,
            Title     = finalTitle,
            Artist    = finalArtist,
            Album     = props.AlbumTitle ?? string.Empty,
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
}
