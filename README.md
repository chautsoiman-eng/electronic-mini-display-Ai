# 電子小螢幕 · Holo Dashboard

ESP8266 / SD2 + **1.54 吋 ST7789，240×240**，配合 Windows 或 macOS 橋接程式，在桌面顯示 AI、電腦狀態、時間、天氣及音樂。保留原有 Claude／Codex 桌寵、網速頁、GIF 與 WiFiManager。

本分支為 **`feature/holo-dashboard`**。軟體建置／自動測試不代表硬體驗證：**尚未收到硬體，五個頁面、USB、接線及 45° 分光鏡全部待實機驗收**。進度見 [TODO](TODO.md)，執行方式見 [驗證說明](docs/HOLO_VERIFICATION.md) 與 [實機驗收表](docs/HARDWARE_ACCEPTANCE.md)。[English](README.en.md)

## 五個 240×240 頁面

| 頁面／模式 | 顯示內容 | 資料來源及限制 |
|---|---|---|
| AI Monitor `holo_ai` | Claude／Codex 各 5 小時、7 天用量、重設倒數、工作狀態與寵物 | 本機 CLI 登入憑據與會話；未知额度顯示 `--`，不當作 0% |
| PC Monitor `pc` | CPU／GPU 負載及溫度、RAM、CPU 歷史 | Windows 橋接；未取得感測器值時不冒充實測 |
| Clock `clock` | 時、分、日期、星期及簡單天氣 | WiFi NTP；USB-only 用電腦 `#TIME`，NTP 成功後優先 NTP；目前韌體時區為台北 |
| Weather `weather` | 氣溫、體感、濕度、風速、天氣狀態 | Windows Open-Meteo，預設台北；需電腦有網路；過期資料非即時 |
| Music `music` | 封面、中文歌名／歌手、進度、24 條頻譜 | Windows WASAPI 真實 FFT；Mac 支援媒體資訊與圖片，**未實作音訊 FFT** |

Windows 有五頁選單及相應預覽；Mac 目前選單／鏡像支援原有桌寵、網速及音樂，**未實作 Holo／PC／Clock／Weather 的 Mac 預覽與 PC／天氣資料來源**。韌體可用裝置網頁選擇五頁。水平鏡像是裝置輸出設定，桌面鏡像是資料重繪，並非螢幕截圖。AUTO 播放／停止與 AI 待審批優先行為仍需實機驗證。

## 安裝與燒錄

請使用本分支建置或此分支 [GitHub Actions](https://github.com/chautsoiman-eng/electronic-mini-display-Ai/actions/workflows/holo-build.yml) 成功執行的附件。上游網站／上游 Release 並不保證包含 Holo 或本次 USB 功能。

```sh
git clone --single-branch --branch feature/holo-dashboard https://github.com/chautsoiman-eng/electronic-mini-display-Ai.git
cd electronic-mini-display-Ai
python3 -m venv firmware/.pio-venv
# Windows 可改用 py -m venv，並以 firmware/.pio-venv/Scripts/python.exe 執行下列步驟
firmware/.pio-venv/bin/python -m pip install platformio==6.1.19
firmware/.pio-venv/bin/python -m platformio run -d firmware -e nodemcuv2
```

燒錄前關閉橋接程式及所有串口監視器，使用 USB **資料線**。CH340／CH341 或 CP210x 應出現為 Windows `COMx` 或 Mac `/dev/cu.usbserial*`、`/dev/cu.wchusbserial*`、`/dev/cu.SLAB_USBtoUART*`；系統未識別時才向晶片原廠取得適用驅動，不能假定每台 Mac 都免驅動。用 `python -m serial.tools.list_ports -v` 確認，再指定實際埠：

```sh
# 使用上述虛擬環境的 python；替換 PORT，勿照用 platformio.ini 的作者本機埠
python -m platformio run -d firmware -e nodemcuv2 -t upload --upload-port PORT
```

SD2 接線為 SCLK GPIO14、MOSI GPIO13、CS GPIO15、DC GPIO0、RST GPIO2、背光 GPIO5（低電位亮）。裸板／不同模組必須按 [platformio.ini](firmware/platformio.ini) 和 [硬體開發說明](docs/DEVELOPMENT.md) 核對電壓與接線後才通電。

### macOS（macOS 12+，Swift 5.9+／Xcode Command Line Tools）

```sh
swift build --package-path mac-app -c release
swift test --package-path mac-app
mac-app/.build/release/AIClockBridge
```

程式常駐選單列；首次執行按系統提示允許本地網路。右鍵控制裝置與管理密碼，左鍵打開鏡像及亮度滑桿。Music 使用系統私有 MediaRemote 介面，不同 macOS／播放器可能沒有資料，必須實測；缺資料時顯示 idle。退出程式可用選單「退出」。

### Windows（.NET 8 SDK 建置）

```powershell
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet publish windows-app/AIClockBridge/AIClockBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
```

執行 `dist/windows/AIClockBridge.exe`；亦可從本分支成功 CI 下載 `windows-portable`，解壓後執行。自包含版本不需另外安裝 .NET Runtime。允許可信私人網路上的橋接連線，音訊選用正在播放的 Windows 輸出裝置。感測器是否可讀與權限／硬體有關，不能保證每台都有 GPU 溫度。

## WiFi、USB 與鏡像

- **WiFi**：首次開機連 `AI-Clock-Setup`，開啟 `192.168.4.1` 配置 2.4 GHz 網路。電腦及裝置需可互通；橋接 HTTP 埠為 `8765`。自動配對失敗可在選單填裝置 IP，並在裝置網頁配置電腦的 `IP:8765`。
- **USB-only**：接資料線，啟動橋接，等待自動掃描與 `#HELLO`／`#DEVICE` 握手（候選埠失敗最多等 30 秒後輪換）。不用先設定 WiFi 或裝置 IP。Mac 會推 `#STATUS`、`#NET`、`#TIME`、`#MUSIC`；Windows 另提供 `#PC`、`#WEATHER`／FFT。電腦仍需網路才能取得新用量或天氣。
- **音樂圖片**：Mac／Windows 按 `#NEED` 傳送 `#IMG` 封面與文字行；115200 baud 下首次圖片逐步出現，並非即時影片。Mac 每 125 ms 最多開始一幀，定期狀態優先。快速換歌會丟棄舊版本待傳圖片。
- **USB 精靈圖與 Mac 鏡像**：`#INFO` 提供模式及版本，`#CMD sprite_dump`／`#SPR` 回傳原始 RGB565；按連線、slot、版本及尺寸快取。首次載入可能需約 25–40 秒；60 秒未完成會失敗並容許重試，斷線清除快取。GIF 上傳／重設仍使用 WiFi HTTP，USB 回傳不等於支援 USB 上傳。
- **切換／斷線**：有線已連接時，控制優先 USB。Mac 超過 12 秒無裝置回覆會關閉並重掃；新連線重新握手。韌體約 15 秒無 serial frame 才離開 wired-active，WiFi 能否恢復依可達性而定。USB 拔線會同時斷電的板子需要合適的獨立供電才可測不中斷 failover。
- **水平鏡像**：Windows 選單、Mac「水平鏡像（45° 分光鏡）」或裝置網頁設定；USB 指令為 `#CMD {"mirror":true}`。套用於韌體全畫面且會保存。用非對稱文字／箭頭實測反射方向；1.54 吋不保證任何特定鏡片尺寸完全不裁切。

## 管理密碼與安全

裝置管理密碼預設空白。設定後，修改裝置的 HTTP API 使用 Basic Auth，使用者名稱 `admin`；橋接選單可儲存／設定密碼。**HTTP 沒有 TLS，Basic Auth 不提供傳輸加密**；只在可信局域網使用，不要把裝置或 `8765` 埠公開到網際網路。唯讀狀態仍可能包含使用資訊；Mac 密碼保存在 UserDefaults、裝置保存在 LittleFS，並非安全憑據保管庫。

忘記密碼：以 USB 實體連線握手後，在管理密碼選單設新密碼或清空；也可關閉橋接後用 115200 baud 工具送 `#HELLO` 再送 `#CMD {"admin_password":""}`（每行換行）。USB 以實體存取作授權，不需舊密碼。請在驗收表記錄复原測試，勿記錄真實密碼。橋接 `/event` 僅接受本機 loopback，請勿繞過此限制。不要提交 CLI token、登入資料、真實密碼或含憑據的日誌。

## 開發與驗證

`firmware/` 為 PlatformIO 韌體，`mac-app/` 為 Swift 選單列程式，`windows-app/` 為 .NET 8 WinForms，`docs/` 保存協定與驗收說明。只在 `feature/holo-dashboard` 開發；不要修改或合併 `main`。

完整重現指令、CI 與限制見 [HOLO_VERIFICATION](docs/HOLO_VERIFICATION.md)。硬體到手後逐項填 [HARDWARE_ACCEPTANCE](docs/HARDWARE_ACCEPTANCE.md)，只有完成實測且有證據才能標為 PASS。
