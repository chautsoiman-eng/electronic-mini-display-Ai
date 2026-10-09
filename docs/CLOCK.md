# Clock 時鐘畫面

## 使用

Windows 無裝置預覽：`AIClockBridge.exe --clock-preview`，或從托盤選單開啟
「时钟（本机预览，无需设备）」。連接電子鐘後選「屏幕显示 → 时钟」，亦可向裝置
`POST /api/display` 傳送 `mode=clock`，或使用 USB 指令
`#CMD {"display":"clock"}`。

## 時間與時區

ESP8266 連線後透過 SNTP 使用 `pool.ntp.org` 與 `time.google.com` 校時。
預設時區是 `Asia/Taipei`（UTC+8）；韌體以可覆寫的 `CLOCK_TIMEZONE`（POSIX 字串，
預設 `CST-8`）及 `CLOCK_TIMEZONE_LABEL` 設定。Windows renderer 使用系統的
`Taipei Standard Time`／`Asia/Taipei` 時區資料。

尚未取得可信 NTP 時間時，畫面顯示 `--:--`、`---- -- --` 與
`WAITING FOR NTP`，不把開機相對時間當作真實時間。成功校時後，即使 Wi-Fi 暫時斷線，
ESP8266 系統時鐘仍會繼續走時。動態區域每秒檢查，但只在分鐘、日期或同步狀態改變時
局部重畫，靜態標題和天氣區不會反覆全畫面刷新。

## 天氣介面

專案目前沒有可重用的真實天氣 API。Windows 使用 `IClockWeatherSource` /
`WeatherSnapshot`，韌體使用獨立的 `ClockWeather` 狀態。未接入來源時溫度及狀態顯示
`--`／`WEATHER --`；時鐘不依賴天氣成功才能運作。這個介面供後續 Weather 頁或 API
接入，現階段沒有假裝即時的 fixture 資料。

## 版面與驗證

Windows `ClockScene.Draw` 與韌體 `drawClockChrome`／`drawClockDynamic` 共用原生
240×240 座標、14 px 主要安全邊距、黑底、白色主資訊及青藍色輔助資訊。

```powershell
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet run --project windows-app/Clock.Tests/Clock.Tests.csproj -c Release -- previews
python -m platformio run --project-dir firmware --environment nodemcuv2
```

Clock 測試涵蓋台北時區、24 小時格式、跨日、日期與星期、分鐘更新鍵、未同步狀態、
缺失天氣與 240×240 安全邊界。輸出的 `clock-*-test.png` 由正式 C# renderer 產生，
並在畫面右上角標示 `TEST DATA`。電子鐘尚未到貨，NTP、ST7789 字型尺寸、局部重畫
與斷線後走時仍需在實機驗證。
