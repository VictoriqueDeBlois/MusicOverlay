namespace MusicOverlay.Core.Models;

public class MediaInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;

    /// <summary>Base64-encoded cover image (JPEG), or empty string if unavailable.</summary>
    public string CoverBase64 { get; set; } = string.Empty;

    /// <summary>Source identifier that produced this info (e.g. "smtc", "netease").</summary>
    public string SourceId { get; set; } = string.Empty;

    public bool IsPlaying { get; set; } = false;

    /// <summary>Returns true if this instance carries meaningful data.</summary>
    public bool HasData => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

    public bool Equals(MediaInfo? other)
    {
        if (other is null) return false;
        return Title == other.Title &&
               Artist == other.Artist &&
               Album == other.Album &&
               IsPlaying == other.IsPlaying &&
               CoverBase64 == other.CoverBase64;
    }
}
