using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;  // requires <UseWindowsForms>true</UseWindowsForms> in .csproj
using MusicOverlay.Config;
using MusicOverlay.Core;
using MusicOverlay.Core.WebServer;
using Newtonsoft.Json;
using Application = System.Windows.Application;

namespace MusicOverlay;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public static ConfigManager      Config  { get; private set; } = null!;
    public static MediaSourceManager Manager { get; private set; } = null!;
    public static OverlayServer      Server  { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Config  = new ConfigManager();
        Manager = new MediaSourceManager(Config);
        Server  = new OverlayServer(Config.App.ServerPort,
                      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web"),
                      Config.App.ActiveFrontend);

        // Wire media changes → server broadcast
        Manager.MediaChanged += (_, info) =>
        {
            var themeJson = JsonConvert.SerializeObject(Config.GetActiveTheme());
            Server.UpdateMedia(info, themeJson);
        };

        Server.Start();
        await Manager.StartAsync();

        SetupTrayIcon();
        ShowMainWindow();  // Show window on startup
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon    = LoadIcon(),
            Text    = "Music Overlay",
            Visible = true
        };

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("设置");
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        var portItem = new ToolStripMenuItem($"端口: {Config.App.ServerPort}");
        portItem.Enabled = false;
        menu.Items.Add(portItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();
        }
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        Server.Stop();
        Manager.Dispose();
        Shutdown();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        // Try to load a custom icon from WPF resources; fall back to a system icon
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
            var resource = Application.GetResourceStream(uri);
            if (resource != null)
                return new System.Drawing.Icon(resource.Stream);
        }
        catch
        {
            // ignore and fall back
        }
        return SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        Server?.Stop();
        Manager?.Dispose();
        base.OnExit(e);
    }
}
