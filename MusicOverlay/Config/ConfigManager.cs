using System.IO;
using MusicOverlay.Core.Sources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicOverlay.Config;

// ── Config models ─────────────────────────────────────────────────────────────

public class AppConfig
{
    [JsonProperty("active_source")]
    public string ActiveSource { get; set; } = "foobar2000";

    [JsonProperty("active_theme")]
    public string ActiveTheme { get; set; } = "card";

    [JsonProperty("active_frontend")]
    public string ActiveFrontend { get; set; } = "default";

    [JsonProperty("server_port")]
    public int ServerPort { get; set; } = 49090;

    [JsonProperty("sources")]
    public Dictionary<string, JObject> Sources { get; set; } = new();

    [JsonProperty("themes")]
    public Dictionary<string, JObject> Themes { get; set; } = new();
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

    /// <summary>
    /// Regex applied to the raw SMTC title string.
    /// Named groups: (?&lt;title&gt;...) and/or (?&lt;artist&gt;...).
    /// Leave empty to use the raw value.
    /// </summary>
    [JsonProperty("smtc_title_regex")]
    public string SmtcTitleRegex { get; set; } = "";

    /// <summary>
    /// Regex applied to the raw SMTC artist string.
    /// Named groups: (?&lt;artist&gt;...) and/or (?&lt;title&gt;...).
    /// Leave empty to use the raw value.
    /// </summary>
    [JsonProperty("smtc_artist_regex")]
    public string SmtcArtistRegex { get; set; } = "";

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

    /// <summary>
    /// Path to NetEase CloudMusic webdb.dat (SQLite) for historyTracks.
    /// Example: "%LocalAppData%\\NetEase\\CloudMusic\\Library\\webdb.dat"
    /// </summary>
    [JsonProperty("webdb_path")]
    public string WebDbPath { get; set; } = @"%LocalAppData%\NetEase\CloudMusic\Library\webdb.dat";

    [JsonProperty("poll_interval_ms")]
    public int PollIntervalMs { get; set; } = 1000;
}

public class ThemeConfig
{
    [JsonProperty("display_name")]
    public string DisplayName { get; set; } = "New Theme";

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

    public AppConfig App { get; private set; } = new();

    public ConfigManager()
    {
        Directory.CreateDirectory(ConfigDir);
        Load();
    }

    public void Load()
    {
        App = LoadOrCreate(SourcesFile, BuildDefaultAppConfig());
    }

    public void Save() => SaveSources();

    public void SaveSources() => WriteJson(SourcesFile, App);

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

    /// <summary>Get the active theme config.</summary>
    public ThemeConfig GetActiveTheme()
    {
        return GetThemeConfig(App.ActiveTheme);
    }

    /// <summary>Deserialise a specific theme entry into a typed ThemeConfig.</summary>
    public ThemeConfig GetThemeConfig(string themeId)
    {
        if (App.Themes.TryGetValue(themeId, out var jObj))
            return jObj.ToObject<ThemeConfig>() ?? new ThemeConfig();
        return new ThemeConfig();
    }

    /// <summary>Persist a ThemeConfig back into the dictionary.</summary>
    public void SetThemeConfig(string themeId, ThemeConfig cfg)
    {
        App.Themes[themeId] = JObject.FromObject(cfg);
    }

    /// <summary>Build an IMediaSource from the active source config.</summary>
    public IMediaSource BuildActiveSource()
    {
        var id  = App.ActiveSource;
        var cfg = GetSourceConfig(id);

        return cfg.Type switch
        {
            "smtc" => new SmtcMediaSource(id, cfg.PreferredApp, cfg.SmtcTitleRegex, cfg.SmtcArtistRegex, cfg.PollIntervalMs),
            "window_capture" => new WindowCaptureSource(
                id,
                cfg.ProcessName,
                cfg.TitleRegex,
                cfg.WebDbPath,
                cfg.PollIntervalMs),
            _ => new SmtcMediaSource(id, "", "", "", cfg.PollIntervalMs)
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
        ActiveSource = "foobar2000",
        ActiveTheme = "card",
        ActiveFrontend = "default",
        ServerPort   = 49090,
        Sources = new Dictionary<string, JObject>
        {
            ["smtc_system"] = JObject.FromObject(new SourceConfig
            {
                Type        = "smtc",
                DisplayName = "系统媒体 (SMTC)",
                PreferredApp = "",
                PollIntervalMs = 1000
            }),
            ["netease_cloudmusic"] = JObject.FromObject(new SourceConfig
            {
                Type        = "window_capture",
                DisplayName = "网易云音乐",
                ProcessName = "cloudmusic.exe",
                // Regex: parse "歌曲名 - 艺术家 - 网易云音乐" from window title.
                // Adjust the suffix pattern if your version uses a different separator.
                TitleRegex  = @"^(?<title>.+?)\s*[-–]\s*(?<artist>.+?)\s*$",
                WebDbPath   = @"%LocalAppData%\NetEase\CloudMusic\Library\webdb.dat",
                PollIntervalMs = 2000
            }),
            ["foobar2000"] = JObject.FromObject(new SourceConfig
            {
                Type        = "smtc",
                DisplayName = "Foobar2000",
                PreferredApp = "foobar2000.exe",
                PollIntervalMs = 1000
            }),
            ["apple_music"] = JObject.FromObject(new SourceConfig
            {
                Type        = "smtc",
                DisplayName = "Apple Music",
                PreferredApp = "AppleMusic",
                PollIntervalMs = 1000
            }),
            ["qq_music"] = JObject.FromObject(new SourceConfig
            {
                Type        = "smtc",
                DisplayName = "QQ音乐",
                PreferredApp = "QQMusic",
                PollIntervalMs = 1000
            })
        },
        Themes = new Dictionary<string, JObject>
        {
            ["vinyl"] = JObject.FromObject(new ThemeConfig
            {
                DisplayName = "黑胶主题",
                Preset = "vinyl",
                Cover = new CoverTheme { Size = 200, Shape = "circle", Animation = "rotate", RotationSpeed = 20 },
                Title = new TextTheme  { Font = "Microsoft YaHei", Size = 28, Color = "#ffffff", Shadow = true, Marquee = true },
                Artist = new TextTheme { Font = "Microsoft YaHei", Size = 18, Color = "#aaaaaa", Shadow = true, Marquee = false },
                Background = new BackgroundTheme { Type = "transparent" }
            }),
            ["minimal"] = JObject.FromObject(new ThemeConfig
            {
                DisplayName = "简约主题",
                Preset = "minimal",
                Cover = new CoverTheme { Size = 180, Shape = "rounded", Animation = "none", RotationSpeed = 0 },
                Title = new TextTheme  { Font = "Microsoft YaHei", Size = 24, Color = "#ffffff", Shadow = false, Marquee = true },
                Artist = new TextTheme { Font = "Microsoft YaHei", Size = 16, Color = "#cccccc", Shadow = false, Marquee = false },
                Background = new BackgroundTheme { Type = "transparent", Color = "#00000000" }
            }),
            ["card"] = JObject.FromObject(new ThemeConfig
            {
                DisplayName = "卡片主题",
                Preset = "card",
                Cover = new CoverTheme { Size = 160, Shape = "rounded", Animation = "pulse", RotationSpeed = 0 },
                Title = new TextTheme  { Font = "Microsoft YaHei", Size = 26, Color = "#ffffff", Shadow = true, Marquee = true, Bold = true },
                Artist = new TextTheme { Font = "Microsoft YaHei", Size = 18, Color = "#e0e0e0", Shadow = true, Marquee = false },
                Background = new BackgroundTheme { Type = "blur_cover", Color = "#00000088" }
            })
        }
    };
}
