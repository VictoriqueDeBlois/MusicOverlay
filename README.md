# Music Overlay

将正在播放的音乐信息（封面、标题、艺术家）实时推送到 OBS 浏览器源。

## 环境要求

- Windows 10 1903+ / Windows 11
- .NET 8 SDK（[下载](https://dotnet.microsoft.com/download)）
- Visual Studio 2022 或 Rider（可选，也可命令行编译）

## 编译 & 运行

```bash
cd MusicOverlay
dotnet build
dotnet run --project MusicOverlay
```

或用 Visual Studio 打开 `MusicOverlay.sln` 直接运行。

## 使用方式

1. 启动程序后会**最小化到系统托盘**，双击托盘图标打开设置窗口。
2. 在"状态"标签页看到服务地址，复制到 OBS 浏览器源。
3. 在"音乐源"标签页选择/配置音乐来源，点击"应用"切换。
4. 在"外观"标签页调整样式，点击"保存此主题"即时生效。

## OBS 浏览器源地址

| 路由 | 内容 |
|------|------|
| `http://localhost:49090/cover`  | 仅封面 |
| `http://localhost:49090/title`  | 仅标题 |
| `http://localhost:49090/artist` | 仅艺术家 |
| `http://localhost:49090/album`  | 仅专辑 |
| `http://localhost:49090/api/now` | JSON 数据（调试用） |

## 项目架构

```
MusicOverlay/
├── App.xaml / App.xaml.cs          # 入口、托盘图标
├── MainWindow.xaml / .cs           # 设置 UI（3 Tab：音乐源 / 外观 / 状态）
├── Config/
│   └── ConfigManager.cs            # AppConfig / SourceConfig / ThemeConfig + JSON 持久化
└── Core/
    ├── Models/MediaInfo.cs          # 媒体信息数据模型
    ├── MediaSourceManager.cs        # 音乐源切换与事件分发
    ├── Sources/
    │   ├── IMediaSource.cs          # 音乐源接口
    │   ├── SmtcMediaSource.cs       # Windows SMTC 集成（含正则提取）
    │   └── NeteaseWebDbSource.cs    # 网易云 webdb 读取 + 正则解析
    └── WebServer/
        └── OverlayServer.cs         # HTTP + WebSocket 服务器

config/
└── sources.json    # 运行时配置（音乐源 + 主题）

web/
└── templates/
    └── default/    # 默认前端模板（可自定义）
```

## 配置文件

所有配置保存在程序目录的 `config/sources.json`，可通过 GUI 编辑，也可直接修改文件。

---

### SMTC 类型

适用于 Spotify、Apple Music、QQ 音乐等支持 Windows 系统媒体控制的软件。

```json
{
  "type": "smtc",
  "display_name": "Spotify",
  "preferred_app": "spotify.exe",
  "smtc_title_regex": "",
  "smtc_artist_regex": "",
  "smtc_album_regex": "",
  "title_regex": "",
  "poll_interval_ms": 1000
}
```

**字段说明：**

- `preferred_app`：
  - 留空 = 自动选当前播放会话
  - 桌面应用填进程名，如 `spotify.exe`
  - Microsoft Store 应用填 AUMID 子串，如 `applemusic`（Apple Music 完整 AUMID 为 `AppleInc.AppleMusicWin_nzyj5cx40ttqa!App`）
  - 匹配逻辑为 `Contains`，大小写不敏感

- `smtc_title_regex`：对 SMTC 返回的**标题字段**应用正则，可提取 `(?<title>...)` / `(?<artist>...)` / `(?<album>...)`，留空不处理
- `smtc_artist_regex`：对 SMTC 返回的**艺术家字段**应用正则，可提取 `(?<artist>...)` / `(?<title>...)` / `(?<album>...)`，留空不处理
- `smtc_album_regex`：对 SMTC 返回的**专辑字段**应用正则，可提取 `(?<album>...)` / `(?<title>...)` / `(?<artist>...)`，留空不处理
- `title_regex`：可选，从 **SMTC 对应进程** 的窗口标题提取 `title/artist/album`（进程名使用 `preferred_app`）

**正则合并优先级：**

| 最终字段 | 优先级顺序 |
|---------|-----------|
| title  | `smtc_title_regex[title]` → `smtc_artist_regex[title]` → `smtc_album_regex[title]` → `title_regex[title]` → 原始 title |
| artist | `smtc_artist_regex[artist]` → `smtc_title_regex[artist]` → `smtc_album_regex[artist]` → `title_regex[artist]` → 原始 artist |
| album  | `smtc_album_regex[album]` → `smtc_title_regex[album]` → `smtc_artist_regex[album]` → `title_regex[album]` → 原始 album |

**使用场景举例：**

某些软件把 `"艺术家 - 歌曲名"` 放在 title 字段，artist 字段为空，此时填入：
```
smtc_title_regex: ^(?<artist>.+?) - (?<title>.+)$
```
即可正确分离标题和艺术家。

---

### 网易云 (WebDB) 类型

适用于网易云等无 SMTC 支持的软件，从窗口标题用正则解析信息，并从 webdb 获取封面与补充字段。

```json
{
  "type": "netease_webdb",
  "display_name": "网易云音乐",
  "process_name": "cloudmusic.exe",
  "title_regex": "^(?<title>.+?)\\s*[-–]\\s*(?<artist>.+?)\\s*$",
  "webdb_path": "%LocalAppData%\\NetEase\\CloudMusic\\Library\\webdb.dat",
  "poll_interval_ms": 1000
}
```

**字段说明：**

- `title_regex`：从窗口标题提取信息，支持命名组 `(?<title>...)` / `(?<artist>...)` / `(?<album>...)`
- `webdb_path`：网易云 `webdb.dat` 路径（SQLite），读取播放历史中的封面 URL 并下载
- `process_name`：网易云进程名（用于读取窗口标题）

---

## 网易云封面配置说明

网易云音乐没有 SMTC 支持，封面获取方式为读取 `webdb.dat` 中的播放历史并下载封面 URL。
如有自定义安装路径，可在 `webdb_path` 中手动指定。

## 前端模板

详见 [前端模板系统使用指南.md](前端模板系统使用指南.md)。

自定义模板放在 `web/templates/<模板名>/`，在设置窗口"状态"标签页切换，切换后需刷新 OBS 浏览器源。
