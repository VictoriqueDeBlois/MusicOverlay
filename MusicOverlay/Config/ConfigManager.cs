using System.IO;
using MusicOverlay.Core.Sources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicOverlay.Config;

// ── Config models ─────────────────────────────────────────────────────────────

public class AppConfig
{
    [JsonProperty("active_source")]
    public string ActiveSource { get; set; } = "smtc_default";

    [JsonProperty("server_port")]
    public int ServerPort { get; set; } = 9090;

    [JsonProperty("sources")]
    public Dictionary<string, JObject> Sources { get; set; } = new();
}

public class SourceConfig
{
    [JsonProperty("type")]
    public string Type { get; set; } = "smtc";

    [JsonProperty("display_name")]
    public string DisplayName { get; set; } = "New Source";

    // --- SMTC options ---
    [JsonProperty("preferred_app")]
    public string PreferredApp { get; set; } = "";

    // --- WindowCapture options ---
    [JsonProperty("process_name")]
    public string ProcessName { get; set; } = "";

    /// <summary>
    /// Regex to parse title and artist from the window title.
    /// Must contain named groups (?&lt;title&gt;...) and (?&lt;artist&gt;...).
    /// Example for NetEase: "^(?&lt;title&gt;.+?)\\s*[-–]\\s*(?&lt;artist&gt;.+?)\\s*[-–]\\s*网易云音乐$"
    /// </summary>
    [JsonProperty("title_regex")]
    public string TitleRegex { get; set; } = "";

    /// <summary>"cache" or "screenshot"</summary>
    [JsonProperty("cover_source")]
    public string CoverSource { get; set; } = "cache";

    /// <summary>
    /// Absolute or %-variable path to the local cover cache directory.
    /// Example: "%AppData%\\Local\\Netease\\CloudMusic\\Cache\\Avatar"
    /// </summary>
    [JsonProperty("cache_path")]
    public string CachePath { get; set; } = "";

    /// <summary>
    /// Relative crop region (0.0–1.0) applied when cover_source = "screenshot".
    /// Tune these values to frame the album art in the target app window.
    /// </summary>
    [JsonProperty("screenshot_crop")]
    public ScreenshotCropConfig ScreenshotCrop { get; set; } = new();

    [JsonProperty("poll_interval_ms")]
    public int PollIntervalMs { get; set; } = 1000;
}

public class ScreenshotCropConfig
{
    [JsonProperty("x")]      public double X { get; set; } = 0.0;
    [JsonProperty("y")]      public double Y { get; set; } = 0.0;
    [JsonProperty("width")]  public double Width { get; set; } = 1.0;
    [JsonProperty("height")] public double Height { get; set; } = 1.0;
}

public class ThemeConfig
{
    [JsonProperty("preset")]
    public string Preset { get; set; } = "vinyl";

    [JsonProperty("cover")]
    public CoverTheme Cover { get; set; } = new();

    [JsonProperty("title")]
    public TextTheme Title { get; set; } = new();

    [JsonProperty("artist")]
    public TextTheme Artist { get; set; } = new();

    [JsonProperty("background")]
    public BackgroundTheme Background { get; set; } = new();
}

public class CoverTheme
{
    [JsonProperty("size")]           public int Size { get; set; } = 200;
    [JsonProperty("shape")]          public string Shape { get; set; } = "circle";
    [JsonProperty("animation")]      public string Animation { get; set; } = "rotate";
    [JsonProperty("rotation_speed")] public int RotationSpeed { get; set; } = 8;
}

public class TextTheme
{
    [JsonProperty("font")]      public string Font { get; set; } = "Microsoft YaHei";
    [JsonProperty("size")]      public int Size { get; set; } = 24;
    [JsonProperty("color")]     public string Color { get; set; } = "#ffffff";
    [JsonProperty("shadow")]    public bool Shadow { get; set; } = true;
    [JsonProperty("marquee")]   public bool Marquee { get; set; } = true;
    [JsonProperty("bold")]      public bool Bold { get; set; } = false;
}

public class BackgroundTheme
{
    /// <summary>"blur_cover" | "color" | "transparent"</summary>
    [JsonProperty("type")]  public string Type { get; set; } = "transparent";
    [JsonProperty("color")] public string Color { get; set; } = "#00000080";
}

// ── Manager ───────────────────────────────────────────────────────────────────

public class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config");

    private static readonly string SourcesFile = Path.Combine(ConfigDir, "sources.json");
    private static readonly string ThemeFile   = Path.Combine(ConfigDir, "theme.json");

    public AppConfig App { get; private set; } = new();
    public ThemeConfig Theme { get; private set; } = new();

    public ConfigManager()
    {
        Directory.CreateDirectory(ConfigDir);
        Load();
    }

    public void Load()
    {
        App   = LoadOrCreate(SourcesFile, BuildDefaultAppConfig());
        Theme = LoadOrCreate(ThemeFile, BuildDefaultThemeConfig());
    }

    public void Save()
    {
        WriteJson(SourcesFile, App);
        WriteJson(ThemeFile, Theme);
    }

    public void SaveSources() => WriteJson(SourcesFile, App);
    public void SaveTheme()   => WriteJson(ThemeFile, Theme);

    /// <summary>Deserialise a specific source entry into a typed SourceConfig.</summary>
    public SourceConfig GetSourceConfig(string sourceId)
    {
        if (App.Sources.TryGetValue(sourceId, out var jObj))
            return jObj.ToObject<SourceConfig>() ?? new SourceConfig();
        return new SourceConfig();
    }

    /// <summary>Persist a SourceConfig back into the dictionary.</summary>
    public void SetSourceConfig(string sourceId, SourceConfig cfg)
    {
        App.Sources[sourceId] = JObject.FromObject(cfg);
    }

    /// <summary>Build an IMediaSource from the active source config.</summary>
    public IMediaSource BuildActiveSource()
    {
        var id  = App.ActiveSource;
        var cfg = GetSourceConfig(id);

        return cfg.Type switch
        {
            "smtc" => new SmtcMediaSource(id, cfg.PreferredApp, cfg.PollIntervalMs),
            "window_capture" => new WindowCaptureSource(
                id,
                cfg.ProcessName,
                cfg.TitleRegex,
                cfg.CoverSource,
                cfg.CachePath,
                new CropRect(
                    cfg.ScreenshotCrop.X,
                    cfg.ScreenshotCrop.Y,
                    cfg.ScreenshotCrop.Width,
                    cfg.ScreenshotCrop.Height),
                cfg.PollIntervalMs),
            _ => new SmtcMediaSource(id, "", cfg.PollIntervalMs)
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static T LoadOrCreate<T>(string path, T defaultValue) where T : class
    {
        if (!File.Exists(path))
        {
            WriteJson(path, defaultValue);
            return defaultValue;
        }
        try
        {
            var text = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(text) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void WriteJson(string path, object obj)
    {
        var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    private static AppConfig BuildDefaultAppConfig() => new()
    {
        ActiveSource = "smtc_default",
        ServerPort   = 9090,
        Sources = new Dictionary<string, JObject>
        {
            ["smtc_default"] = JObject.FromObject(new SourceConfig
            {
                Type        = "smtc",
                DisplayName = "系统媒体 (SMTC)",
                PreferredApp = "",
                PollIntervalMs = 1000
            }),
            ["netease"] = JObject.FromObject(new SourceConfig
            {
                Type        = "window_capture",
                DisplayName = "网易云音乐",
                ProcessName = "cloudmusic.exe",
                // Regex: parse "歌曲名 - 艺术家 - 网易云音乐" from window title.
                // Adjust the suffix pattern if your version uses a different separator.
                TitleRegex  = @"^(?<title>.+?)\s*[-–]\s*(?<artist>.+?)\s*[-–]\s*网易云音乐$",
                CoverSource = "cache",
                // Adjust this path to match your NetEase installation / Windows username.
                CachePath   = @"%AppData%\Local\Netease\CloudMusic\Cache\Avatar",
                ScreenshotCrop = new ScreenshotCropConfig { X = 0.04, Y = 0.10, Width = 0.38, Height = 0.65 },
                PollIntervalMs = 2000
            })
        }
    };

    private static ThemeConfig BuildDefaultThemeConfig() => new()
    {
        Preset = "vinyl",
        Cover = new CoverTheme { Size = 200, Shape = "circle", Animation = "rotate", RotationSpeed = 8 },
        Title = new TextTheme  { Font = "Microsoft YaHei", Size = 28, Color = "#ffffff", Shadow = true, Marquee = true },
        Artist = new TextTheme { Font = "Microsoft YaHei", Size = 18, Color = "#aaaaaa", Shadow = true, Marquee = false },
        Background = new BackgroundTheme { Type = "transparent" }
    };
}
