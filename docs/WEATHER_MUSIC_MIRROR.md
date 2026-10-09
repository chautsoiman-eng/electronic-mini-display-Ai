# Weather、Music FFT 與水平鏡像

## Weather

Windows `WeatherMonitor` 從 Open-Meteo current weather API 取得溫度、體感溫度、濕度、
風速和 WMO weather code，無需 API key。預設座標為台北 `25.0330,121.5654`，可從托盤
修改。橋接的 `GET /weather` 只回傳仍在 30 分鐘有效期內的資料；韌體每 10 分鐘更新，
Clock 與獨立 Weather 頁共用相同資料。失敗時保留時鐘功能並顯示 `--`。
Weather 頁依 WMO code 原生繪製晴天、局部多雲、陰天、霧、雨、陣雨、雪與雷暴圖形；
沒有有效天氣資料時不顯示圖形，避免把未知狀態冒充成真實天氣。

## 24 條音頻頻譜

Windows 使用 NAudio 2.4.0 的 WASAPI loopback 擷取系統輸出。2048 點 Hann window FFT
映射到 55 Hz–16 kHz 的 24 個對數頻帶，經衰減平滑後附加在既有 `/music` JSON。
裝置與 Windows 鏡像在專輯封面及歌名下方畫相同 24 條頻譜，最底部保留播放進度。
沒有輸出裝置、擷取失敗或超過 2 秒沒有資料時，頻譜回到零，不影響 Now Playing。

## 水平鏡像

`POST /api/mirror enabled=1` 讓 ST7789 使用 MADCTL MX 位元翻轉原生畫面，設定保存於
LittleFS。Windows 鏡像用相同的 240×240 水平變換。這可供 45° 全息反射配置使用；
最終方向與面板批次仍需在實機確認。

## 驗證

`Dashboard.Tests` 驗證 Open-Meteo JSON、WMO code、缺失資料、24-bar FFT、440 Hz 頻帶、
0–100 邊界、240×240 安全邊距及水平鏡像，並輸出 Weather、缺資料、鏡像與 Music FFT PNG。
