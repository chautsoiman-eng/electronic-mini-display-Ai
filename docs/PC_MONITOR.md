# PC 監控

## 使用

沒有電子鐘：`AIClockBridge.exe --pc-preview` 開啟本機真實資料預覽。一般托盤模式亦提供獨立預覽入口。預覽標示 LIVE PREVIEW，不是實機截圖。

有電子鐘：Windows bridge 正常啟動後，選「屏幕显示 → PC 监控」，或向裝置 `POST /api/display` 傳送 `mode=pc`。

## 來源與限制

| 欄位 | 來源／定義 |
|---|---|
| CPU | GetSystemTimes 前後差值；首次採樣未知 |
| RAM | GlobalMemoryStatusEx 實體記憶體使用百分比 |
| GPU MAX | Windows PDH GPU Engine，同一實體 engine 的各 process 相加，取各卡各 engine 最高使用率；不等同不同工具的平均值。不可用時讀 NVIDIA SMI 各卡最高負載 |
| CPU TEMP MAX | 已存在的 LibreHardwareMonitor 或 OpenHardwareMonitor WMI provider 中 CPU 感測器最高溫。不讀 ACPI thermal zone 當作核心溫度 |
| GPU TEMP MAX | NVIDIA SMI 各 NVIDIA GPU 的最高溫；其他品牌目前顯示未知。多 GPU 時負載與最高溫可能來自不同卡 |
| CPU HISTORY | 每秒一點，最多 60 點；休眠／採樣空檔以 null 留白 |

程式不安裝感測器驅動、不自動提高權限。溫度背景更新約 5 秒；15 秒後過期。核心資料超過 5 秒未更新時，畫面標示 DATA STALE 並隱藏即時讀數。未知值以 `--` 顯示，曲線不跨過未知點。

資料來源參考：[Microsoft PDH](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw)、[NVIDIA SMI](https://docs.nvidia.com/deploy/nvidia-smi/index.html)。

## 傳輸協定

Bridge `GET /pc` 回傳 JSON；韌體每秒輪詢。USB 接收端支援 `#PC {json}\n`、切換使用 `#CMD {"display":"pc"}\n`。Windows 自動 USB 傳輸尚未實作，正常使用走 HTTP。

```json
{"ts":1791468000,"seq":12,"interval_ms":1000,"cpu_pct":35,"gpu_pct":10,"mem_pct":75,"cpu_temp_c":null,"gpu_temp_c":33,"cpu_history":[null,30,35],"stale":false}
```

`ts` 是 Unix 秒，`seq` 是採樣序號，重啟後可歸零。數值未知用 null。`cpu_history` 由舊到新、最多 60 筆，每筆 0–100 或 null；JSON 加上 serial 前綴須小於 1600 bytes。韌體拒絕破損 JSON、缺失必要中繼資料、超長歷史與非 1000ms 間隔；異常數字降為未知。重複 ts/seq 不延長新鮮度；HTTP 失敗 5 秒後隱藏讀數。只收到 #STATUS 不會抑制 /pc 輪詢，只有最近 #PC 才會。

## 驗證

```powershell
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet run --project windows-app/PcMonitor.Tests -c Release -- previews
# 本機真實採樣（CI 不要求 GPU／感測器）
dotnet run --project windows-app/PcMonitor.Tests -c Release -- previews --live
python -m platformio run -d firmware -e nodemcuv2
```

測試包含數值界限、未知值、GPU 聚合、NVIDIA 不支援回覆、歷史上限與缺口、逾時、JSON 格式／大小、真實 localhost HTTP server，以及正常／未知／過期畫面。`--live` 額外採樣 7 秒並輸出 pc-live.json / pc-live.png。本機已觀察到 CPU、GPU、RAM、GPU 溫度；CPU 溫度無 provider，正確保持 null。

電子鐘尚未到貨。到貨後仍需測試 PC 與原模式切換、HTTP／#PC 更新、拔線超過 5 秒的過期畫面、重連與 bridge 重啟、60 點邊界，以及 240×240 面板長時間刷新。現有 ImageSharp 3.1.12 的 NuGet 安全性警告不屬於本次 PC 監控修改，仍存在。
