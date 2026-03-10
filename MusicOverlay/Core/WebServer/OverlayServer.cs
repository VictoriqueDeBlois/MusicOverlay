using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using MusicOverlay.Core.Models;
using Newtonsoft.Json;

namespace MusicOverlay.Core.WebServer;

/// <summary>
/// Lightweight HTTP + WebSocket server.
///
/// HTTP routes (all served from the /web directory next to the executable):
///   GET /             → web/index.html      (full overlay)
///   GET /cover        → web/cover.html      (cover only)
///   GET /title        → web/title.html      (title only)
///   GET /artist       → web/artist.html     (artist only)
///   GET /api/now      → JSON of current MediaInfo
///   GET /api/theme    → JSON of current ThemeConfig
///   GET /static/*     → static files from web/ (js, css, images)
///   GET /*.js|css|png|jpg → root-level static files from web/
///
/// WebSocket:
///   ws://localhost:{port}/ws  → push MediaInfo JSON on every change
/// </summary>
public class OverlayServer : IDisposable
{
    private readonly int _port;
    private readonly string _webRootBase;
    private string _currentTemplate;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    private readonly List<WebSocket> _wsClients = new();
    private readonly object _wsLock = new();

    private MediaInfo _currentInfo = new();
    private string _themeJson = "{}";

    public bool IsRunning { get; private set; }

    public OverlayServer(int port, string webRootBase, string initialTemplate = "default")
    {
        _port = port;
        _webRootBase = webRootBase;
        _currentTemplate = initialTemplate;
    }

    /// <summary>Switch to a different frontend template (e.g. "default", "custom1").</summary>
    public void SetTemplate(string templateName)
    {
        _currentTemplate = templateName;
    }

    private string GetTemplateRoot() => Path.Combine(_webRootBase, "templates", _currentTemplate);

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        IsRunning = true;
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
    }

    /// <summary>Update the cached media info and broadcast to all WS clients.</summary>
    public void UpdateMedia(MediaInfo info, string themeJson)
    {
        _currentInfo = info;
        _themeJson   = themeJson;
        _ = BroadcastAsync(BuildMediaPayload(info, themeJson));
    }

    // ── HTTP accept loop ──────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleContextAsync(ctx, ct);
            }
            catch (HttpListenerException) { break; }
            catch { /* keep running */ }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        try
        {
            // CORS for local dev
            resp.Headers.Add("Access-Control-Allow-Origin", "*");

            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "/";

            // WebSocket upgrade
            if (req.IsWebSocketRequest && path == "/ws")
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                _ = HandleWebSocketAsync(wsCtx.WebSocket, ct);
                return;
            }

            // API routes
            if (path == "/api/now")
            {
                await WriteJsonAsync(resp, BuildMediaPayload(_currentInfo, _themeJson));
                return;
            }
            if (path == "/api/theme")
            {
                await WriteJsonAsync(resp, _themeJson);
                return;
            }

            // HTML overlay routes
            var htmlFile = path switch
            {
                "/"       or "/index"  => "index.html",
                "/cover"               => "cover.html",
                "/title"               => "title.html",
                "/artist"              => "artist.html",
                _                      => null
            };

            if (htmlFile != null)
            {
                await ServeFileAsync(resp, Path.Combine(GetTemplateRoot(), htmlFile));
                return;
            }

            // Static files from template directory (e.g. /custom/user.css → templates/{name}/custom/user.css)
            if (path.StartsWith("/static/") || path.StartsWith("/custom/"))
            {
                var filePath = Path.Combine(GetTemplateRoot(), path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                await ServeFileAsync(resp, filePath);
                return;
            }

            // Shared themes directory (e.g. /themes/vinyl.css → web/themes/vinyl.css)
            if (path.StartsWith("/themes/"))
            {
                var filePath = Path.Combine(_webRootBase, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                await ServeFileAsync(resp, filePath);
                return;
            }

            // Root-level static files (e.g. /overlay.js → templates/{name}/overlay.js)
            if (path.Length > 1 && !path.Contains(".."))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".js" || ext == ".css" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".ico")
                {
                    var filePath = Path.Combine(GetTemplateRoot(), path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(filePath))
                    {
                        await ServeFileAsync(resp, filePath);
                        return;
                    }
                }
            }

            resp.StatusCode = 404;
            resp.Close();
        }
        catch
        {
            try { resp.StatusCode = 500; resp.Close(); } catch { }
        }
    }

    // ── WebSocket ─────────────────────────────────────────────────────────────

    private async Task HandleWebSocketAsync(WebSocket ws, CancellationToken ct)
    {
        lock (_wsLock) _wsClients.Add(ws);

        // Send current state immediately on connect
        await SendWsAsync(ws, BuildMediaPayload(_currentInfo, _themeJson));

        var buf = new byte[256];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buf, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
            }
        }
        catch { }
        finally
        {
            lock (_wsLock) _wsClients.Remove(ws);
            ws.Dispose();
        }
    }

    private async Task BroadcastAsync(string json)
    {
        List<WebSocket> snapshot;
        lock (_wsLock) snapshot = _wsClients.ToList();

        foreach (var ws in snapshot)
        {
            try { await SendWsAsync(ws, json); }
            catch { }
        }
    }

    private static async Task SendWsAsync(WebSocket ws, string json)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string BuildMediaPayload(MediaInfo info, string themeJson)
    {
        var obj = new
        {
            title    = info.Title,
            artist   = info.Artist,
            album    = info.Album,
            cover    = info.CoverBase64.Length > 0 ? "data:image/jpeg;base64," + info.CoverBase64 : "",
            isPlaying = info.IsPlaying,
            sourceId = info.SourceId,
            theme    = themeJson.Length > 2 ? JsonConvert.DeserializeObject(themeJson) : null
        };
        return JsonConvert.SerializeObject(obj);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse resp, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentType     = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private static async Task ServeFileAsync(HttpListenerResponse resp, string filePath)
    {
        if (!File.Exists(filePath))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        resp.ContentType = ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css"  => "text/css; charset=utf-8",
            ".js"   => "application/javascript; charset=utf-8",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico"  => "image/x-icon",
            _       => "application/octet-stream"
        };

        var bytes = await File.ReadAllBytesAsync(filePath);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    public void Dispose() => Stop();
}
