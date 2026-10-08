# AIClockBridge for Windows

`mac-app/` 菜单栏桥接的 Windows 移植版：同一套功能、同一套设备协议（固件感知不到
桥接跑在哪个系统上），以系统托盘图标形式常驻。

功能与 Mac 版一致：

- **左键托盘图标** → ESP8266 屏幕实时镜像（额度环 + 桌宠动画 + 网速图 + 音乐页，
  与设备渲染同一份数据），底部附 自动/Claude/Codex/网速/音乐 快速切换
- **右键托盘图标** → 控制菜单：Claude/Codex 完整额度（5h/周 + 重置倒计时）、
  自动查找并配对设备、设置设备地址、屏幕显示模式、petdex 桌宠画廊、恢复默认动画、
  把本机设为设备桥接、桥接服务地址
- 本地 HTTP 服务 `0.0.0.0:8765`：`/status`、`/net`、`/music`、`/music/cover.raw`、
  `/music/text.raw`、`POST /event`（Claude Code / Codex hooks 秒级状态推送）
- 数据来源同 Mac 版：`%USERPROFILE%\.claude\projects` / `%USERPROFILE%\.codex\sessions`
  的 JSONL 日志 + 各自官方用量接口（凭据读
  `%USERPROFILE%\.claude\.credentials.json` 和 `%USERPROFILE%\.codex\auth.json`，
  token 只发给各自官方 API）
- 音乐页读系统级 Now Playing（WinRT `GlobalSystemMediaTransportControlsSessionManager`，
  Spotify / 浏览器 / 本地播放器都能识别）；网速取物理网卡（以太网/WiFi）字节计数，
  4Hz 采样，排除 VPN/虚拟网卡

与 Mac 版的差异：

- 无固件刷写入口（刷写请用网页版刷写工具）
- 图像处理依赖 [Magick.NET 14.17.2](https://github.com/dlemstra/Magick.NET)（Apache 2.0，無需授權金鑰）——
  System.Drawing 解不了 petdex 的 WebP 精灵图、也编不了多帧 GIF

## 构建 / 运行

### PC 監控（不需要電子鐘）

啟動 `AIClockBridge.exe --pc-preview` 可直接查看本機 CPU、GPU、RAM、可用溫度與 60 秒 CPU 曲線；此模式不啟動 OAuth 用量讀取、HTTP server 或裝置配對。
一般托盤模式亦有「PC 监控（本机预览，无需设备）」；連接電子鐘後，可在「屏幕显示 → PC 监控」切到相同資料的裝置頁面。

GPU 使用 Windows GPU Engine 計數器，NVIDIA 工具為 fallback；CPU 溫度需已有 LibreHardwareMonitor/OpenHardwareMonitor 的 WMI 感測服務，NVIDIA GPU 溫度由既有 nvidia-smi 取得。缺少、失敗或不支援的數值顯示 `--`，不會自動安裝驅動或要求管理員權限。新增 `System.Management` 依賴僅用來讀取既有 WMI provider。
詳見 [PC 監控協定與驗證](../docs/PC_MONITOR.md)。

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows 10
19041+ / Windows 11）：

```powershell
cd windows-app\AIClockBridge
dotnet run                # 前台运行（托盘出现小电脑图标）
# 或发布单文件：
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# 产物在 bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\AIClockBridge.exe
```

首次启动 Windows 会弹防火墙授权（HTTP 服务监听 0.0.0.0:8765，设备要从局域网访问，
选"允许"）。

**开机自启**：`Win+R` → `shell:startup` → 把 `AIClockBridge.exe` 的快捷方式放进去。

**Hooks 实时状态**（可选，同主 README §7）：Claude Code / Codex 的 hooks 往
`http://127.0.0.1:8765/event` POST 事件即可，Windows 下 curl 自带。

## 验证

```powershell
curl.exe -s http://localhost:8765/status | python -m json.tool
```

配置持久化在 `%APPDATA%\AIClockBridge\settings.json`（设备地址等）。

## 代码结构

| 文件 | 对应 Mac 版 | 说明 |
|---|---|---|
| `Program.cs` | `main.swift` | 入口 + 路由表 + 被动发现 |
| `TrayAppContext.cs` | `MenuBarController.swift` | 托盘图标 + 控制菜单 |
| `MirrorForm.cs` | `MirrorPopover.swift` | 240x240 屏幕镜像弹窗 |
| `PetPickerForm.cs` | `PetPickerWindow.swift` | petdex 桌宠选择器 |
| `PetdexService.cs` | `PetdexService.swift` | manifest / 精灵图 / GIF 合成 |
| `StatusService.cs` | `StatusReader.swift` | JSONL 日志扫描 + hook 事件 |
| `UsageFetcher.cs` | `UsageFetcher.swift` | 官方额度接口 |
| `NetSpeedMonitor.cs` | `NetSpeedMonitor.swift` | 4Hz 网速采样环 |
| `NowPlayingMonitor.cs` | `NowPlayingMonitor.swift` | 系统 Now Playing + 封面/文字条 RGB565 |
| `DeviceClient.cs` | `DeviceClient.swift` | 设备 HTTP API + 自动配对/子网扫描 |
| `MiniHttpServer.cs` | `HTTPServer.swift` | 0.0.0.0:8765 极简 HTTP 服务 |
| `Rgb565.cs` | （MirrorPopover 内联） | RGB565 大端编解码 |


## 桌寵圖片函式庫驗證

ImageSharp 已移除。使用 Magick.NET-Q8-AnyCPU 14.17.2 與間接依賴
Magick.NET.Core 14.17.2，保留 8×9 WebP 圖集（每格 192×208）、九種動畫、
Claude 111×120 / Codex 120×120 黑底 GIF、最多八幀及無限循環。
幀延遲沿用原本的百分之一秒向下取整，最低 50 ms。

背景轉檔先在 UI 執行緒取得獨立圖集副本；換角色、清除選擇、關閉視窗
不會提前釋放背景工作使用中的圖片。GIF 預覽的 MemoryStream 保持到圖片卸除後才釋放。
過時預覽或尚未送出的上傳會因選擇版本改變而取消。

    dotnet run --project windows-app/Petdex.Tests -c Release
    dotnet run --project windows-app/HoloAi.Tests -c Release -- previews
    dotnet run --project windows-app/PcMonitor.Tests -c Release -- previews
    dotnet list windows-app/AIClockBridge package --vulnerable --include-transitive

Petdex.Tests 使用可重現的透明 WebP 圖集與 Windows GDI+ 獨立 GIF 解碼，
檢查尺寸、裁切列/幀、幀數、幀延遲、循環、透明轉黑底，以及 WinForms
切換選擇、上傳轉檔及關窗時的資源生命週期。測試裝置上傳為 stub，並非實機驗證。
CI 執行以上回歸、NuGet 漏洞檢查與 Windows x64 自包含打包。
已知 NuGet 漏洞 NU1901–NU1904 視為建置錯誤；沒有隱藏漏洞警告。

發佈包包含 Magick.NET-Notice.txt（含原生 ImageMagick 與其他內含函式庫公告）。
複製整個 publish 目錄即可執行，不用另外安裝 .NET 或 ImageMagick。
電子鐘尚未到貨，裝置端 GIF 解碼與上傳仍需日後實機測試。

本機驗證（2026-10-08）：Release 0 警告／0 錯誤；桌寵離線 374 項，
加上真實 petdex manifest/WebP 轉檔共 379 項；Holo 27 項，
PC 回歸 30 項（含本機即時採樣為 33 項）通過。
NuGet 含間接依賴檢查未列出已知漏洞，這是當日資料庫結果，並非永久安全保證。
版本與授權來源：[NuGet](https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU/14.17.2)、
[上游授權](https://github.com/dlemstra/Magick.NET/blob/main/License.txt)。
