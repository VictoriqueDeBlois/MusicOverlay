using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MusicOverlay.Config;
using MusicOverlay.Core.Models;
using Newtonsoft.Json.Linq;

namespace MusicOverlay;

public partial class MainWindow : Window
{
    private ConfigManager Config => App.Config;
    private string? _editingSourceId;
    private string? _editingThemeId;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        App.Manager.MediaChanged += OnMediaChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var port = Config.App.ServerPort;
        ServerStatusText.Text = $"HTTP 服务运行在 http://localhost:{port}/";

        SetupUrls(port);
        LoadSourceList();
        LoadThemeList();
        LoadFrontendList();
        RefreshStatus(App.Manager.CurrentInfo);
    }

    // ── Status tab ───────────────────────────────────────────────────────────

    private void SetupUrls(int port)
    {
        // UrlFull.Text   = $"http://localhost:{port}/          (完整 overlay)";
        UrlCover.Text  = $"http://localhost:{port}/cover     (仅封面)";
        UrlTitle.Text  = $"http://localhost:{port}/title     (仅标题)";
        UrlArtist.Text = $"http://localhost:{port}/artist    (仅艺术家)";
        UrlApi.Text    = $"http://localhost:{port}/api/now   (JSON 数据)";

        // Store the bare URL in Tag for click-to-open
        // UrlFull.Tag   = $"http://localhost:{port}/";
        UrlCover.Tag  = $"http://localhost:{port}/cover";
        UrlTitle.Tag  = $"http://localhost:{port}/title";
        UrlArtist.Tag = $"http://localhost:{port}/artist";
        UrlApi.Tag    = $"http://localhost:{port}/api/now";
    }

    private void OnMediaChanged(object? sender, MediaInfo info)
    {
        Dispatcher.Invoke(() => RefreshStatus(info));
    }

    private void RefreshStatus(MediaInfo info)
    {
        StatusTitle.Text  = info.Title  is { Length: > 0 } t ? t : "(无)";
        StatusArtist.Text = info.Artist is { Length: > 0 } a ? a : "(无)";
        StatusSource.Text = info.SourceId is { Length: > 0 } s ? s : "(无)";
        StatusCover.Text  = info.CoverBase64.Length > 0 ? $"已获取 ({info.CoverBase64.Length / 1024} KB)" : "无封面";
    }

    private void OpenUrl(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBlock tb && tb.Tag is string url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void LoadFrontendList()
    {
        FrontendSelector.Items.Clear();

        var templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "templates");
        if (Directory.Exists(templatesDir))
        {
            foreach (var dir in Directory.GetDirectories(templatesDir))
            {
                var name = Path.GetFileName(dir);
                FrontendSelector.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = name, Tag = name
                });
            }
        }

        // Select active
        foreach (System.Windows.Controls.ComboBoxItem item in FrontendSelector.Items)
        {
            if (item.Tag as string == Config.App.ActiveFrontend)
            {
                FrontendSelector.SelectedItem = item;
                break;
            }
        }

        // Fallback to first item if active not found
        if (FrontendSelector.SelectedItem == null && FrontendSelector.Items.Count > 0)
            FrontendSelector.SelectedIndex = 0;
    }

    private void FrontendSelector_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FrontendSelector.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string templateName)
        {
            Config.App.ActiveFrontend = templateName;
            Config.SaveSources();
            App.Server.SetTemplate(templateName);
        }
    }

    // ── Sources tab ──────────────────────────────────────────────────────────

    private void LoadSourceList()
    {
        SourceList.Items.Clear();
        SourceSelector.Items.Clear();

        foreach (var kv in Config.App.Sources)
        {
            var cfg = Config.GetSourceConfig(kv.Key);
            var typeLabel = cfg.Type switch
            {
                "smtc" => "系统媒体 (SMTC)",
                "netease_webdb" => "网易云 (WebDB)",
                "window_capture" => "网易云 (WebDB)",
                _ => cfg.Type
            };
            var label = $"{cfg.DisplayName}  [{kv.Key}]  ({typeLabel})";
            SourceList.Items.Add(new System.Windows.Controls.ListBoxItem
            {
                Content = label, Tag = kv.Key
            });
            SourceSelector.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = cfg.DisplayName, Tag = kv.Key
            });
        }

        // Select active
        foreach (System.Windows.Controls.ComboBoxItem item in SourceSelector.Items)
        {
            if (item.Tag as string == Config.App.ActiveSource)
            {
                SourceSelector.SelectedItem = item;
                break;
            }
        }
    }

    private void SourceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is System.Windows.Controls.ListBoxItem li && li.Tag is string id)
            OpenSourceEditor(id);
    }

    private void OpenSourceEditor(string sourceId)
    {
        _editingSourceId = sourceId;
        var cfg = Config.GetSourceConfig(sourceId);

        EditorDisplayName.Text      = cfg.DisplayName;
        EditorPreferredApp.Text     = cfg.PreferredApp;
        EditorSmtcTitleRegex.Text   = cfg.SmtcTitleRegex;
        EditorSmtcArtistRegex.Text  = cfg.SmtcArtistRegex;
        EditorProcessName.Text      = cfg.ProcessName;
        EditorTitleRegex.Text       = cfg.TitleRegex;
        EditorWebDbPath.Text        = cfg.WebDbPath;
        EditorPollInterval.Text     = cfg.PollIntervalMs.ToString();

        // Type selector
        var editorType = cfg.Type == "window_capture" ? "netease_webdb" : cfg.Type;
        foreach (System.Windows.Controls.ComboBoxItem item in EditorType.Items)
            if (item.Tag as string == editorType) { EditorType.SelectedItem = item; break; }

        ApplyEditorTypeVisibility(cfg.Type);
        SourceEditor.Visibility = Visibility.Visible;
    }

    private void ApplyEditorTypeVisibility(string type)
    {
        bool isCapture = type == "netease_webdb" || type == "window_capture";
        var smtcVis    = isCapture ? Visibility.Collapsed : Visibility.Visible;
        var captureVis = isCapture ? Visibility.Visible   : Visibility.Collapsed;

        // SMTC-only fields
        LblPreferredApp.Visibility    = smtcVis; PanelPreferredApp.Visibility    = smtcVis;
        LblSmtcTitleRegex.Visibility  = smtcVis; PanelSmtcTitleRegex.Visibility  = smtcVis;
        LblSmtcArtistRegex.Visibility = smtcVis; PanelSmtcArtistRegex.Visibility = smtcVis;

        // WindowCapture-only fields
        LblProcessName.Visibility = captureVis; EditorProcessName.Visibility = captureVis;
        LblTitleRegex.Visibility  = captureVis; PanelTitleRegex.Visibility   = captureVis;
        if (!isCapture)
        {
            LblWebDbPath.Visibility = Visibility.Collapsed; EditorWebDbPath.Visibility = Visibility.Collapsed;
        }
        else
        {
            LblWebDbPath.Visibility = Visibility.Visible; EditorWebDbPath.Visibility = Visibility.Visible;
        }
    }

    private void EditorType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EditorType.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            ApplyEditorTypeVisibility(item.Tag as string ?? "smtc");
    }

    private void SaveSourceEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSourceId == null) return;

        var cfg = Config.GetSourceConfig(_editingSourceId);
        cfg.DisplayName      = EditorDisplayName.Text.Trim();
        cfg.PreferredApp     = EditorPreferredApp.Text.Trim();
        cfg.SmtcTitleRegex   = EditorSmtcTitleRegex.Text.Trim();
        cfg.SmtcArtistRegex  = EditorSmtcArtistRegex.Text.Trim();
        cfg.ProcessName      = EditorProcessName.Text.Trim();
        cfg.TitleRegex       = EditorTitleRegex.Text.Trim();
        cfg.WebDbPath        = EditorWebDbPath.Text.Trim();
        if (int.TryParse(EditorPollInterval.Text, out var ms)) cfg.PollIntervalMs = ms;

        if (EditorType.SelectedItem is System.Windows.Controls.ComboBoxItem typeItem)
            cfg.Type = typeItem.Tag as string ?? "smtc";

        Config.SetSourceConfig(_editingSourceId, cfg);
        Config.SaveSources();
        LoadSourceList();
        System.Windows.MessageBox.Show("已保存。如果此源当前正在使用，请重新应用以生效。", "保存成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        var id = $"source_{Guid.NewGuid().ToString()[..6]}";
        Config.SetSourceConfig(id, new SourceConfig { Type = "smtc", DisplayName = "新音乐源" });
        Config.SaveSources();
        LoadSourceList();
        OpenSourceEditor(id);
    }

    private void DeleteSource_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSourceId == null) return;
        if (_editingSourceId == Config.App.ActiveSource)
        {
            System.Windows.MessageBox.Show("无法删除当前正在使用的源，请先切换到其他源。", "提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Config.App.Sources.Remove(_editingSourceId);
        Config.SaveSources();
        _editingSourceId = null;
        SourceEditor.Visibility = Visibility.Collapsed;
        LoadSourceList();
    }

    private void SourceSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

    private async void ApplySource_Click(object sender, RoutedEventArgs e)
    {
        if (SourceSelector.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string id)
        {
            await App.Manager.SwitchSourceAsync(id);
            LoadSourceList();
            System.Windows.MessageBox.Show($"已切换到：{item.Content}", "已应用", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ── Theme tab ────────────────────────────────────────────────────────────

    private void LoadThemeList()
    {
        ThemeList.Items.Clear();
        ThemeSelector.Items.Clear();

        foreach (var kv in Config.App.Themes)
        {
            var cfg = Config.GetThemeConfig(kv.Key);
            var label = $"{cfg.DisplayName}  [{kv.Key}]";
            ThemeList.Items.Add(new System.Windows.Controls.ListBoxItem
            {
                Content = label, Tag = kv.Key
            });
            ThemeSelector.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = cfg.DisplayName, Tag = kv.Key
            });
        }

        // Select active
        foreach (System.Windows.Controls.ComboBoxItem item in ThemeSelector.Items)
        {
            if (item.Tag as string == Config.App.ActiveTheme)
            {
                ThemeSelector.SelectedItem = item;
                break;
            }
        }
    }

    private void ThemeList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeList.SelectedItem is System.Windows.Controls.ListBoxItem li && li.Tag is string id)
            OpenThemeEditor(id);
    }

    private void OpenThemeEditor(string themeId)
    {
        _editingThemeId = themeId;
        var t = Config.GetThemeConfig(themeId);

        EditorThemeName.Text = t.DisplayName;

        foreach (System.Windows.Controls.ComboBoxItem item in PresetSelector.Items)
            if (item.Tag as string == t.Preset) { PresetSelector.SelectedItem = item; break; }

        CoverSize.Text          = t.Cover.Size.ToString();
        CoverRotationSpeed.Text = t.Cover.RotationSpeed.ToString();
        foreach (System.Windows.Controls.ComboBoxItem item in CoverShape.Items)
            if (item.Tag as string == t.Cover.Shape) { CoverShape.SelectedItem = item; break; }
        foreach (System.Windows.Controls.ComboBoxItem item in CoverAnimation.Items)
            if (item.Tag as string == t.Cover.Animation) { CoverAnimation.SelectedItem = item; break; }

        TitleFont.Text    = t.Title.Font;
        TitleSize.Text    = t.Title.Size.ToString();
        TitleColor.Text   = t.Title.Color;
        TitleShadow.IsChecked  = t.Title.Shadow;
        TitleMarquee.IsChecked = t.Title.Marquee;
        TitleBold.IsChecked    = t.Title.Bold;

        ArtistFont.Text    = t.Artist.Font;
        ArtistSize.Text    = t.Artist.Size.ToString();
        ArtistColor.Text   = t.Artist.Color;
        ArtistShadow.IsChecked  = t.Artist.Shadow;
        ArtistMarquee.IsChecked = t.Artist.Marquee;
        ArtistBold.IsChecked    = t.Artist.Bold;

        foreach (System.Windows.Controls.ComboBoxItem item in BgType.Items)
            if (item.Tag as string == t.Background.Type) { BgType.SelectedItem = item; break; }
        BgColor.Text = t.Background.Color;

        ThemeEditor.Visibility = Visibility.Visible;
    }

    private void PresetSelector_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetSelector.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var preset = item.Tag as string ?? "vinyl";
            // Apply preset defaults to form controls
            ApplyPresetDefaults(preset);
        }
    }

    private void ApplyPresetDefaults(string preset)
    {
        switch (preset)
        {
            case "vinyl":
                // Vinyl preset: 黑胶旋转效果
                CoverSize.Text = "200";
                CoverRotationSpeed.Text = "8";
                SelectComboItem(CoverShape, "circle");
                SelectComboItem(CoverAnimation, "rotate");

                TitleFont.Text = "Microsoft YaHei";
                TitleSize.Text = "28";
                TitleColor.Text = "#ffffff";
                TitleShadow.IsChecked = true;
                TitleMarquee.IsChecked = true;
                TitleBold.IsChecked = false;

                ArtistFont.Text = "Microsoft YaHei";
                ArtistSize.Text = "18";
                ArtistColor.Text = "#aaaaaa";
                ArtistShadow.IsChecked = true;
                ArtistMarquee.IsChecked = false;
                ArtistBold.IsChecked = false;

                SelectComboItem(BgType, "transparent");
                BgColor.Text = "#00000080";
                break;

            case "minimal":
                // Minimal preset: 简约风格，无动画
                CoverSize.Text = "180";
                CoverRotationSpeed.Text = "0";
                SelectComboItem(CoverShape, "rounded");
                SelectComboItem(CoverAnimation, "none");

                TitleFont.Text = "Microsoft YaHei";
                TitleSize.Text = "24";
                TitleColor.Text = "#ffffff";
                TitleShadow.IsChecked = false;
                TitleMarquee.IsChecked = true;
                TitleBold.IsChecked = false;

                ArtistFont.Text = "Microsoft YaHei";
                ArtistSize.Text = "16";
                ArtistColor.Text = "#cccccc";
                ArtistShadow.IsChecked = false;
                ArtistMarquee.IsChecked = false;
                ArtistBold.IsChecked = false;

                SelectComboItem(BgType, "transparent");
                BgColor.Text = "#00000000";
                break;

            case "card":
                // Card preset: 卡片风格，带背景
                CoverSize.Text = "160";
                CoverRotationSpeed.Text = "0";
                SelectComboItem(CoverShape, "rounded");
                SelectComboItem(CoverAnimation, "pulse");

                TitleFont.Text = "Microsoft YaHei";
                TitleSize.Text = "26";
                TitleColor.Text = "#ffffff";
                TitleShadow.IsChecked = true;
                TitleMarquee.IsChecked = true;
                TitleBold.IsChecked = true;

                ArtistFont.Text = "Microsoft YaHei";
                ArtistSize.Text = "18";
                ArtistColor.Text = "#e0e0e0";
                ArtistShadow.IsChecked = true;
                ArtistMarquee.IsChecked = false;
                ArtistBold.IsChecked = false;

                SelectComboItem(BgType, "blur_cover");
                BgColor.Text = "#00000088";
                break;
        }
    }

    private void SelectComboItem(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if (item.Tag as string == tag)
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void SaveThemeEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_editingThemeId == null) return;

        var t = Config.GetThemeConfig(_editingThemeId);

        t.DisplayName = EditorThemeName.Text.Trim();

        if (PresetSelector.SelectedItem is System.Windows.Controls.ComboBoxItem presetItem)
            t.Preset = presetItem.Tag as string ?? "vinyl";

        if (int.TryParse(CoverSize.Text, out var cs))  t.Cover.Size  = cs;
        if (int.TryParse(CoverRotationSpeed.Text, out var rs)) t.Cover.RotationSpeed = rs;
        if (CoverShape.SelectedItem     is System.Windows.Controls.ComboBoxItem shapeItem)
            t.Cover.Shape     = shapeItem.Tag as string ?? "circle";
        if (CoverAnimation.SelectedItem is System.Windows.Controls.ComboBoxItem animItem)
            t.Cover.Animation = animItem.Tag as string ?? "rotate";

        t.Title.Font    = TitleFont.Text.Trim();
        t.Title.Color   = TitleColor.Text.Trim();
        t.Title.Shadow  = TitleShadow.IsChecked  == true;
        t.Title.Marquee = TitleMarquee.IsChecked == true;
        t.Title.Bold    = TitleBold.IsChecked    == true;
        if (int.TryParse(TitleSize.Text, out var tSize)) t.Title.Size = tSize;

        t.Artist.Font    = ArtistFont.Text.Trim();
        t.Artist.Color   = ArtistColor.Text.Trim();
        t.Artist.Shadow  = ArtistShadow.IsChecked  == true;
        t.Artist.Marquee = ArtistMarquee.IsChecked == true;
        t.Artist.Bold    = ArtistBold.IsChecked    == true;
        if (int.TryParse(ArtistSize.Text, out var aSize)) t.Artist.Size = aSize;

        if (BgType.SelectedItem is System.Windows.Controls.ComboBoxItem bgItem)
            t.Background.Type = bgItem.Tag as string ?? "transparent";
        t.Background.Color = BgColor.Text.Trim();

        Config.SetThemeConfig(_editingThemeId, t);
        Config.SaveSources();
        LoadThemeList();

        // If this is the active theme, push to overlays immediately
        if (_editingThemeId == Config.App.ActiveTheme)
        {
            var themeJson = Newtonsoft.Json.JsonConvert.SerializeObject(t);
            App.Server.UpdateMedia(App.Manager.CurrentInfo, themeJson);
        }

        System.Windows.MessageBox.Show("主题已保存。如果要应用到 overlay，请在上方选择并点击'应用'。", "保存成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddTheme_Click(object sender, RoutedEventArgs e)
    {
        var id = $"theme_{Guid.NewGuid().ToString()[..6]}";
        Config.SetThemeConfig(id, new ThemeConfig
        {
            DisplayName = "新主题",
            Preset = "card"
        });
        Config.SaveSources();
        LoadThemeList();
        OpenThemeEditor(id);
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (_editingThemeId == null) return;
        if (_editingThemeId == Config.App.ActiveTheme)
        {
            System.Windows.MessageBox.Show("无法删除当前正在使用的主题，请先切换到其他主题。", "提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Config.App.Themes.Remove(_editingThemeId);
        Config.SaveSources();
        _editingThemeId = null;
        ThemeEditor.Visibility = Visibility.Collapsed;
        LoadThemeList();
    }

    private void ThemeSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

    private async void ApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string id)
        {
            Config.App.ActiveTheme = id;
            Config.SaveSources();

            // Push updated theme to connected overlay pages immediately
            var theme = Config.GetThemeConfig(id);
            var themeJson = Newtonsoft.Json.JsonConvert.SerializeObject(theme);
            App.Server.UpdateMedia(App.Manager.CurrentInfo, themeJson);

            LoadThemeList();
            System.Windows.MessageBox.Show($"已应用主题：{item.Content}", "已应用", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ── Window behaviour ─────────────────────────────────────────────────────

    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Intercept close → hide to tray instead of destroying the window
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Manager.MediaChanged -= OnMediaChanged;
        base.OnClosed(e);
    }
}
