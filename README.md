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
4. 在"外观"标签页调整样式，点击"保存外观设置"即时生效。

## OBS 浏览器源地址

| 路由 | 内容 |
|------|------|
| `http://localhost:9090/`      | 完整 overlay（封面+标题+艺术家） |
| `http://localhost:9090/cover`  | 仅封面 |
| `http://localhost:9090/title`  | 仅标题 |
| `http://localhost:9090/artist` | 仅艺术家 |
| `http://localhost:9090/api/now` | JSON 数据（调试用） |

## 配置文件

所有配置保存在程序目录的 `config/` 文件夹：

### `config/sources.json`

每个音乐源单独配置，支持两种类型：

**SMTC 类型**（适用于 Spotify、QQ音乐、Apple Music 等）：
```json
{
  "type": "smtc",
  "display_name": "Spotify",
  "preferred_app": "spotify.exe",
  "poll_interval_ms": 1000
}
```
- `preferred_app`：留空=自动选当前播放会话；填进程名=固定来源

**窗口捕获类型**（适用于网易云等无 SMTC 的软件）：
```json
{
  "type": "window_capture",
  "display_name": "网易云音乐",
  "process_name": "cloudmusic.exe",
  "title_regex": "^(?<title>.+?)\\s*[-–]\\s*(?<artist>.+?)\\s*[-–]\\s*网易云音乐$",
  "cover_source": "cache",
  "cache_path": "%AppData%\\Local\\Netease\\CloudMusic\\Cache\\Avatar",
  "screenshot_crop": { "x": 0.04, "y": 0.10, "width": 0.38, "height": 0.65 },
  "poll_interval_ms": 2000
}
```
- `title_regex`：必须包含命名组 `(?<title>...)` 和 `(?<artist>...)`
- `cover_source`：`"cache"` = 读本地缓存文件（推荐）；`"screenshot"` = 截窗口裁剪
- `cache_path`：网易云封面缓存目录，支持 `%AppData%` 等环境变量
- `screenshot_crop`：截图裁剪区域（0.0~1.0 相对比例），仅 `cover_source=screenshot` 时生效

### `config/theme.json`

外观配置，可直接编辑文件或通过 GUI 修改。

### `web/custom/user.css`

自定义 CSS 覆盖文件，直接写 CSS 即可，**无需重启程序**，刷新 OBS 浏览器源生效。

## 网易云封面配置说明

网易云音乐没有 SMTC 支持，封面获取有两种方式：

### 方式一：本地缓存（推荐）

网易云会把封面缓存到本地磁盘。常见路径（因版本和安装位置而异，需自行确认）：

```
%AppData%\Local\Netease\CloudMusic\Cache\Avatar
%AppData%\Roaming\Netease\CloudMusic\webdata\file\avatar
```

建议：打开网易云播放一首歌，然后在资源管理器搜索最近修改的 `.jpg` / `.png` 文件，找到封面缓存目录后填入 `cache_path`。

### 方式二：窗口截图裁剪

将 `cover_source` 设为 `"screenshot"`，调整 `screenshot_crop` 的 x/y/width/height（0.0~1.0 相对值）来裁出封面区域。建议先用截图工具量好坐标再填写。
