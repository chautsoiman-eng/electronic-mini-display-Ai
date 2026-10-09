# Holo AI 驗證

## 已執行

- ESP8266：PlatformIO 6.1.19，`nodemcuv2` 編譯及連結成功，RAM 44148 / 81920 bytes，Flash 840363 / 1044464 bytes。
- Windows：.NET SDK 8.0.425，Release 編譯成功。圖片依賴已在後續提交改為 Magick.NET；CI 會檢查直接與間接 NuGet 漏洞。
- Holo 繪圖測試：未知值、百分比四捨五入／上限、警戒色、重設時間與四條 bar 的像素檢查，另輸出 normal / unknown / offline 預覽。
- 尚未燒錄或驗證實機：本機只列出內建 COM1，沒有電子鐘的 CH340 USB 埠。Windows 預覽使用相同資料及座標重新繪圖，並非裝置截圖，字型可能不同。

## 重現指令（repo 根目錄）

```powershell
python -m pip install platformio==6.1.19
python -m platformio run --project-dir firmware --environment nodemcuv2
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet run --project windows-app/HoloAi.Tests/HoloAi.Tests.csproj -c Release -- previews
```

Windows 若遇到依賴路徑過長，先將 `PLATFORMIO_CORE_DIR` 與 `PLATFORMIO_LIBDEPS_DIR` 設為較短、可寫的絕對路徑。
GitHub Actions 的 `Holo build and render checks` 會建置韌體、Windows app 並保留韌體與預覽附件；遠端是否通過應以該次 workflow 結果為準。

## 實機待驗證

1. 接上電子鐘，執行 `python -m serial.tools.list_ports -v`，確認 CH340 的 COM 埠後，以 `python -m platformio run -d firmware -e nodemcuv2 -t upload --upload-port COMx` 燒錄（替換 COMx，勿使用內建 COM1）。
2. Windows 托盤「屏幕显示」選 Holo AI，確認 Claude／Codex 各兩條 bar 與未知值 `--`，鏡像視窗亦顯示 Holo。
3. 以 HTTP `POST /api/display` 的 `mode=holo_ai` 及 USB `#CMD {"display":"holo_ai"}` 分別切換；持續收到 `#STATUS` 時不得出現寵物圖案。
4. 從音樂、網速切入 Holo，再切回各模式；確認 Holo 標題左對齊、AUTO 音樂播放／停止與審批優先切換正常。在 Holo 上傳／重設寵物圖片，不應覆蓋 Holo。
5. 拔除裝置後 Windows 鏡像顯示 DEVICE OFFLINE；重接後恢復。另需在實機觀察長時間刷新、WiFi 配網與 LittleFS 圖片保存。

PC 監控、時鐘、天氣頁、音訊 FFT 與水平鏡像均已完成（見 `docs/HANDOFF_2026-10-09.md`）；仍待實機驗證。
