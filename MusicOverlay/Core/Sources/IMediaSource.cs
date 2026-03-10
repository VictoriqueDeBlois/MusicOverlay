using MusicOverlay.Core.Models;

namespace MusicOverlay.Core.Sources;

/// <summary>
/// Common interface for all media info sources (SMTC, window capture, etc.)
/// </summary>
public interface IMediaSource : IDisposable
{
    string SourceId { get; }
    bool IsRunning { get; }

    void Start();
    void Stop();

    /// <summary>Fired whenever the currently playing media changes.</summary>
    event EventHandler<MediaInfo> MediaChanged;

    /// <summary>Synchronously fetch the latest media info (used on first load).</summary>
    Task<MediaInfo> GetCurrentAsync();
}
