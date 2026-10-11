# TODO

## 硬體到手前（feature/holo-dashboard）
- [x] Mac USB 音樂：每秒 `#MUSIC`，`#NEED` 後以 `#IMG` 傳 128×128 封面／232×44 文字圖；修訂版本與資料原子讀取、舊圖淘汰。
- [x] Mac USB 裝置資訊／精靈圖：`#INFO`、`#CMD sprite_dump`／`#SPR`，版本／尺寸校驗、連線快取；無 host 也能開桌寵／音樂鏡像。
- [x] Mac serial framing／節流／短寫入保留、壞訊框丟棄、超時、CH340/CP210x 候選輪換及重連清理；新增協定與 pseudo-terminal 測試。
- [x] Mac USB 水平鏡像控制；保留 HTTP fallback、管理密碼及既有 GIF 上傳流程。
- [x] 中英文 README：五頁、平台差異、分支安裝、WiFi／USB、水平鏡像、管理密碼、安全及實機限制。
- [x] [實機驗收 checklist](docs/HARDWARE_ACCEPTANCE.md)：前置條件、操作、預期結果、失敗欄與長時間驗證。
- [ ] 五頁與 USB／光學／密碼復原實機驗收（硬體未到；所有硬體結果 NOT RUN）。

## 自動驗證
- [x] 本機 macOS Swift Release build。
- [x] 本機 PlatformIO 6.1.19 `nodemcuv2` build。
- [x] macOS CI Swift Release build／11 項 tests 全通過，含 pseudo-terminal（未跳過）。本機 `swift test` 因 Command Line Tools 缺 XCTest 無法執行。
- [x] Windows CI Release build、六組 .NET 測試、NuGet 漏洞檢查及 portable 發佈通過。本機 Serial.Tests 受 macOS sandbox `GetDomainName` 限制。
- [x] 實作提交 `56f7688` 的 [GitHub Actions #38128556088](https://github.com/chautsoiman-eng/electronic-mini-display-Ai/actions/runs/38128556088) 三個 jobs 全通過。後續提交僅記錄驗證結果；環境受限項及實機未測仍另列。

## 原有 Windows／韌體功能（程式已實作，待實機）
- [x] Windows CH340/CP210x 掃描與 `#STATUS/#NET/#PC/#WEATHER/#TIME`。
- [x] Windows USB Music `#MUSIC/#NEED/#IMG`、精靈圖 `#INFO/#SPR` 與快取。
- [x] WiFi／USB 控制、裝置管理密碼、bridge `/event` loopback 限制。

## 保留限制／未納入本次 USB 對齊
- [ ] Mac PC／天氣資料來源、音訊 FFT、五頁 Holo 預覽與選單完整對齊 Windows。
- [ ] USB GIF 上傳／重設（現有協定僅支援精靈圖回傳，仍用 HTTP 上傳／重設）。
