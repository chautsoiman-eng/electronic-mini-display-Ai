# Holo Dashboard 驗證

本分支 `feature/holo-dashboard` 的軟體建置／測試與實機驗收分開記錄；不得將編譯或桌面預覽稱為硬體通過。硬體尚未到手，[完整實機表](HARDWARE_ACCEPTANCE.md) 全部為 NOT RUN。

## 2026-10-11 本機驗證

- macOS arm64，Apple Swift 6.3.3：Release build 成功（Command Line Tools）。
- `swift test` 已執行：測試編譯被本機缺少 `XCTest` 阻擋；新增 protocol／pseudo-terminal 測試交由 CI 的 macOS 完整 Xcode 執行。
- PlatformIO 6.1.19，espressif8266 4.2.1，`nodemcuv2`：成功。RAM **47632 / 81920** bytes；Flash **873839 / 1044464** bytes。未燒錄。
- .NET SDK 8.0.425 arm64：已執行 `Serial.Tests`；執行到 HTTP 安全測試時因 macOS sandbox `GetDomainName: -1` 中止，**不能算整套通過**。WinForms 建置／繪圖測試需要 Windows CI。
- `git diff --check`：提交前執行。未修改 Windows 或韌體實作；新增 Mac USB 功能及其測試。

GitHub Actions：[Holo build and render checks](https://github.com/chautsoiman-eng/electronic-mini-display-Ai/actions/workflows/holo-build.yml)。推送此分支自動啟動，應核對 run 的 commit SHA 與待測檔案，不沿用舊提交綠燈。

## 重現指令（repo 根目錄）

macOS（完整 Xcode 提供 XCTest）：

```sh
swift build --package-path mac-app -c release
swift test --package-path mac-app
```

若沙箱不允許預設 cache，將快取移至可寫的暫存路徑（不改系統 HOME）：

```sh
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/holo-module-cache CLANG_MODULE_CACHE_PATH=/tmp/holo-module-cache swift build --package-path mac-app -c release --scratch-path /tmp/holo-swift-build --cache-path /tmp/holo-spm-cache --disable-sandbox
```

此參數不會補上缺失的 XCTest。測試涵蓋 1500-byte framing、中文換行轉義、分段／雜訊接收、兩種音樂圖片逐行 round-trip、精靈圖 offset／revision／尺寸校驗、部分 write 保留，以及 OS pseudo-terminal 的握手、Music、#INFO、精靈圖快取、#IMG、斷線取消、重連與靜默超時。沒有 pseudo-terminal 的環境會標該測試 skipped，不能聲稱該項已跑。

Windows PowerShell（這些測試是 executable harness，使用 `dotnet run` 而非只跑 `dotnet test`）：

```powershell
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet run --project windows-app/HoloAi.Tests/HoloAi.Tests.csproj -c Release -- previews
dotnet run --project windows-app/PcMonitor.Tests/PcMonitor.Tests.csproj -c Release -- previews
dotnet run --project windows-app/Clock.Tests/Clock.Tests.csproj -c Release -- previews
dotnet run --project windows-app/Dashboard.Tests/Dashboard.Tests.csproj -c Release -- previews
dotnet run --project windows-app/Petdex.Tests/Petdex.Tests.csproj -c Release
dotnet run --project windows-app/Serial.Tests/Serial.Tests.csproj -c Release
```

韌體（建議先建立虛擬環境；Windows 用 `py` 或虛擬環境 Python）：

```sh
python -m pip install platformio==6.1.19
python -m platformio run --project-dir firmware --environment nodemcuv2
```

Windows 如遇依賴路徑過長，將 `PLATFORMIO_CORE_DIR` 與 `PLATFORMIO_LIBDEPS_DIR` 指向短且可寫的絕對路徑。CI 另檢查 NuGet 直接／間接漏洞，並保留 firmware.bin、Windows portable 及預覽附件。

## USB 協定與已知限制

- 韌體單行容量1600 bytes；Mac frame限制1500 bytes，125 ms最多開始一幀，定期 feed優先、命令佇列最多32，封面／文字各只保留当前版本。
- 非阻塞 partial write／EAGAIN 保留剩餘 bytes，2秒無法完成關閉串口；收到無效或超長行丟棄至換行。掃描每5秒、握手每3秒、候選30秒超時後輪換；連線12秒無有效裝置回覆就重連。
- `#INFO` 新鮮度6秒；失效回退既有HTTP。`#SPR` 單一傳輸，最多8幀、60秒期限；僅完整且版本／幾何一致才快取，斷線取消 callback並清快取。
- 既有 firmware沒有 `#CMD`／`#IMG` ACK或校驗重傳。queued不等於裝置套用；實機應以 #INFO／畫面驗證。若串口丟行，圖片可能不完整；重進Music或重連讓裝置重新發出 #NEED，並記錄於 M02／M03。
- 首次大圖傳送可能需數秒至數十秒。這不是USB視訊。Mac MediaRemote相容性、ST7789字型／刷新、UART吞吐、實體插拔和LittleFS穩定性均仍待實機。
- Mac沒有PC／天氣資料來源和真實FFT，亦沒有五頁完整桌面預覽；不在這次USB Music／精靈圖對齊的實作範圍。
