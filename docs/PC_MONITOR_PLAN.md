# PC Monitor Implementation Plan

**Goal:** 真實 PC 遙測、無裝置本機預覽、240×240 韌體畫面，保持原有模式。

**Architecture:** Windows 每秒擷取 CPU/RAM/GPU 負載，背景讀取可用溫度；`GET /pc` 提供含 60 點 CPU 歷史的快照。韌體輪詢同一格式，也接受 `#PC`，資料逾時顯示 STALE。

**Tech Stack:** .NET 8 WinForms、Win32/PDH、NVIDIA SMI、既有 Libre/OpenHardwareMonitor WMI provider、ESP8266 ArduinoJson/TFT_eSPI。

## Constraints

- 只提交 feature/holo-dashboard；不安裝硬體驅動，不把未知值當 0。
- GPU 負載取最忙的實體 engine；多張 NVIDIA 卡溫度取最高值，畫面標示 MAX。
- 實機尚未到貨；編譯、資料與繪圖測試不能取代實機驗證。

## Execution

1. `PcTelemetry.cs` 定義快照及有限歷史、`GpuStatsReader.cs` 讀 PDH、`PcMonitor.cs` 非阻塞採樣；改善 `SystemStatsMonitor.cs` 的未知值。測試 null、過期、歷史上限、GPU 聚合及 JSON。
2. `PcMonitorScene.cs` 繪圖與 `PcPreviewForm.cs` 真實資料本機預覽；Program、TrayAppContext、MirrorForm 加入 `pc`。以測試程式輸出正常／未知／離線畫面，再檢查圖片。
3. `firmware/src/main.cpp` 加入 pc 模式、/pc 輪詢、#PC 解析、有限陣列與逾時處理。PlatformIO 編譯、Windows Release 與測試通過後推送，確認 CI，更新驗證文件。

Commands: `dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release`; `dotnet run --project windows-app/PcMonitor.Tests -c Release`; `python -m platformio run -d firmware -e nodemcuv2`.
