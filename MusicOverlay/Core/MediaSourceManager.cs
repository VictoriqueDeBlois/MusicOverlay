using MusicOverlay.Config;
using MusicOverlay.Core.Models;
using MusicOverlay.Core.Sources;

namespace MusicOverlay.Core;

/// <summary>
/// Manages the currently active IMediaSource.
/// Call SwitchSource() to hot-swap the active source without restarting the app.
/// </summary>
public class MediaSourceManager : IDisposable
{
    private readonly ConfigManager _config;
    private IMediaSource? _activeSource;

    public MediaInfo CurrentInfo { get; private set; } = new();

    /// <summary>Fired on the calling thread whenever media info changes.</summary>
    public event EventHandler<MediaInfo>? MediaChanged;

    public MediaSourceManager(ConfigManager config)
    {
        _config = config;
    }

    /// <summary>Start with the source defined in config.</summary>
    public async Task StartAsync()
    {
        var source = _config.BuildActiveSource();
        await AttachAndStartAsync(source);
    }

    /// <summary>
    /// Hot-swap to a different source by its ID.
    /// Stops the current source, saves the new active ID to config, then starts the new source.
    /// </summary>
    public async Task SwitchSourceAsync(string sourceId)
    {
        _config.App.ActiveSource = sourceId;
        _config.SaveSources();

        var source = _config.BuildActiveSource();
        await AttachAndStartAsync(source);
    }

    private async Task AttachAndStartAsync(IMediaSource source)
    {
        // Detach old
        if (_activeSource != null)
        {
            _activeSource.MediaChanged -= OnMediaChanged;
            _activeSource.Stop();
            _activeSource.Dispose();
        }

        _activeSource = source;
        _activeSource.MediaChanged += OnMediaChanged;
        _activeSource.Start();

        // Fetch initial state immediately
        var initial = await _activeSource.GetCurrentAsync();
        OnMediaChanged(this, initial);
    }

    private void OnMediaChanged(object? sender, MediaInfo info)
    {
        CurrentInfo = info;
        MediaChanged?.Invoke(this, info);
    }

    public void Dispose()
    {
        _activeSource?.MediaChanged -= OnMediaChanged;
        _activeSource?.Stop();
        _activeSource?.Dispose();
    }
}
