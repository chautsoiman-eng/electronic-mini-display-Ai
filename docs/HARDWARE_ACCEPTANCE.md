# 實機驗收表：ESP8266 / 1.54 吋 ST7789

**目前所有項目均為 NOT RUN；沒有實機結果。** 請複製此表至獨立驗收紀錄，填寫 PASS／FAIL／BLOCKED／N/A，不能用編譯成功代替實測。N/A 必須填原因。首次驗收每次只啟用一個橋接程式。

## 測試環境

| 欄位 | 填寫 |
|---|---|
| 日期／測試者 | |
| 韌體與 bridge commit（必須相同分支）／CI URL | |
| ESP8266 板型、Flash 大小／ST7789 型號／接線照片 | |
| USB 晶片 CH340/CH341 或 CP210x／埠／驅動版本／資料線 | |
| macOS／Windows 版本、CPU 架構／橋接版本 | |
| 獨立供電方式（如測拔線 failover） | |
| WiFi AP／2.4 GHz／是否 client isolation | |
| 音樂播放器／版本／音訊輸出裝置 | |
| 分光鏡尺寸／R:T／角度／螢幕至鏡片距離 | |

不要把密碼、token、登入檔案或個人歌單放進公開紀錄。失敗日誌先遮蔽敏感資訊。

## 操作方式

燒錄及列埠命令見 [README](../README.md)。串口工具使用 **115200 baud，newline 結尾**；開串口工具前先退出 bridge，避免兩個 reader 爭用。`#CMD` 沒有 ACK，寫入成功不等於裝置成功；用 `#INFO` 或螢幕驗證結果。要取得原始協定證據，可在 bridge 停止時送 `#HELLO`，並每 3 秒重送保持 wired-active。

五頁可用裝置網頁或 Windows 托盤切換。無 WiFi 時可先用串口工具送 `#CMD {"display":"clock"}`（或 `holo_ai`／`weather`／`pc`／`music`），關閉工具再啟動 bridge。Mac 現有選單只包含桌寵、网速與音樂；Mac 不提供 PC、天氣和 FFT，這些資料驗收使用 Windows。

| ID | 步驟 | 預期結果／判定 | 狀態 | 失敗記錄（實際結果、日誌／照片、issue） |
|---|---|---|---|---|
| H01 | 斷電核對 GPIO14/13/15/0/2/5、3.3V 邏輯、共地及背光；拍照 | 與 SD2 設定一致，無短路；不同接線先更改設定再建置 | NOT RUN | |
| H02 | 分別在 Mac／Windows 插入 CH340/CH341 資料線，列埠；拔插再列埠 | 出現／消失正確 cu.*／COM 埠；不是充電線或內建 COM1 | NOT RUN | |
| H03 | 如有 CP210x 板，重做 H02；無此板標 BLOCKED | CP210x 正確識別並可開啟；不能以 CH340 結果代替 | NOT RUN | |
| H04 | 停止 bridge／monitor，指定真實埠上傳 nodemcuv2 韌體，重新啟動 | 燒錄完成、無 boot loop；韌體版本／commit 有紀錄 | NOT RUN | |
| H05 | 顯示高對比頁，檢查四角、240×240 邊緣、RGB 色、旋轉、10/50/100% 亮度 | 無偏移／裁切／反色，亮度方向正確；0% 可從 bridge 恢復 | NOT RUN | |
| N01 | 清除／使用未配網板（須先保存設定），連 AI-Clock-Setup、192.168.4.1，設定 2.4 GHz | 配網完成，重新開機可重連；入口不阻塞後续 USB 資料 | NOT RUN | |
| N02 | USB 只供電，WiFi bridge 配置電腦 IP:8765；測自動及手動配對 | /api/info 可讀，資料持續更新；防火牆只開可信網路 | NOT RUN | |
| N03 | 裝置不配置 WiFi，接 Mac USB，清空 bridge 裝置 host（記下原值） | 自動握手；無 IP 也可顯示狀態、網速、時間與音樂，選單顯示 USB | NOT RUN | |
| N04 | Windows 重做 N03 | 狀態、時間、PC、天氣、音樂／FFT 持續更新；電腦可保留網路 | NOT RUN | |
| N05 | 同時有 WiFi／USB，停用裝置 WiFi／開 AP isolation，再恢復 | 已握手 USB 持續運作；恢復 WiFi 後無重複圖片或互相覆蓋 | NOT RUN | |
| N06 | 可獨立安全供電時拔 USB 資料連線，WiFi 保持連通；否則退出 bridge 的 serial 路徑需另測 | 約 15 秒 wired-active 超時後重新 WiFi 輪詢；記錄恢復耗時；斷電重啟不算無縫 failover | NOT RUN | |
| N07 | 拔插 USB、退出／重啟 bridge、裝置 reset 各 5 次；可選接另一相同 rev 裝置 | 重新握手、定期資料恢復、無舊圖片／舊 #INFO；Mac 無回覆 12 秒後重掃 | NOT RUN | |
| N08 | 同時存在非時鐘 UART 與時鐘，非時鐘排列在前 | 候選握手逾 30 秒後輪換，不永久卡在第一個埠 | NOT RUN | |
| A01 | Holo AI 有有效登入時，與 bridge 當前用量比較；登出／缺用量再測 | 四條 bar、百分比、倒數正確；未知為 --，不是假 0%；不得記錄憑據 | NOT RUN | |
| A02 | 觸發工作／閒置／待審批，再在 AUTO、Holo、桌寵、Music 間切換 | 動畫及提示對應真實狀態，Holo 不被一般 #STATUS 畫面覆蓋；記錄 AUTO 優先順序 | NOT RUN | |
| P01 | Windows PC 頁，空閒及受控 CPU 負載各 2 分鐘，與系統監視器比較 | CPU/RAM／歷史變化合理；可取得的 GPU／溫度更新，未知顯示 -- | NOT RUN | |
| C01 | WiFi 啟用網際網路，進 Clock 等 NTP；比對台北時間 | 日期、星期、跨分鐘更新正確，誤差記錄；無秒跳／整頁閃爍 | NOT RUN | |
| C02 | 裝置無 WiFi，以 Mac、Windows 分別冷開機 USB-only | #TIME 校時（每 60 秒）可用；斷線短時間仍走時；斷電後需重新校時 | NOT RUN | |
| C03 | USB 校時後恢復 NTP，觀察午夜／日期邊界（可用隔離測試時鐘） | NTP 成功後優先，日期／星期跟時區一致，不反覆覆蓋 | NOT RUN | |
| W01 | Windows 設定已知天氣座標，觀察 Weather 及 Clock footer | 溫度、體感、濕度、風速、地點／狀態與來源一致，單位正確 | NOT RUN | |
| W02 | 中斷電腦網際網路超過 30 分鐘，再恢復 | 舊天氣不假裝即時；恢復後下一輪更新；UI 不因 HTTP 失敗卡死 | NOT RUN | |
| M01 | Windows／Mac 分別 WiFi 與 USB-only 播放已知有封面的歌曲，含中文／emoji／長歌名 | AUTO 切 Music，進度與暫停正確；封面128×128、文字232×44完整，無亂碼／顏色倒置 | NOT RUN | |
| M02 | USB-only 首次播放，觀察 #NEED 後封面／文字逐步完成 | 兩種 #IMG 完成，狀態／時間仍更新；記錄全部圖片載入秒數，不能只看第一行 | NOT RUN | |
| M03 | 圖片未完成時快速換歌10次、切出再切回 Music、暫停／停止 | 舊 rev 不覆蓋新歌；新圖最終完整；缺封面可顯示 placeholder；停止回 AUTO 正確頁 | NOT RUN | |
| M04 | Windows 分別播放低音／高音／靜音，變更預設輸出裝置 | 24條 FFT 左低右高、attack/decay/peak 合理、靜音衰減；Mac FFT 明確 N/A | NOT RUN | |
| M05 | Mac 用至少兩個播放器測 MediaRemote；無資訊播放器亦測 | 支援者 metadata 正確；不支援者 idle／缺資料，不假造頻譜或播放狀態 | NOT RUN | |
| S01 | Mac 清空 host，USB-only 打開桌寵鏡像，依序 Claude／Codex | #INFO 可用；兩 slot 首次 #SPR 約25–40秒完成；動畫／顏色／尺寸與實機一致 | NOT RUN | |
| S02 | 同一 slot 反覆開關鏡像，切換 slot，再關閉／重連 USB | 同版本重用快取，不重复 dump；新連線重新取得；資料不可串到另一裝置 | NOT RUN | |
| S03 | WiFi 上傳 GIF／reset，回 USB-only 開鏡像 | sprite_rev 改變，重抓新圖；LittleFS 斷電重啟後保存；USB 不宣稱能上傳 | NOT RUN | |
| S04 | dump 中拔線／更换精靈圖／停止傳送，重連或稍後再開鏡像 | 不顯示半張圖；版本變動失效；60秒超時可重試，無卡死／無限排隊 | NOT RUN | |
| O01 | 45° 分光鏡前顯示非對稱字母、左右箭頭及四角標记；開關水平鏡像 | 反射文字方向正確，整頁及音樂圖一致；直接看螢幕會相應反向 | NOT RUN | |
| O02 | 調整觀看距離、角度及亮度，測黑底各五頁；重啟裝置 | 記錄可視範圍、裁切、重影、亮度；水平鏡像保存；40mm 等尺寸需實測不能預判 PASS | NOT RUN | |
| Q01 | 設管理密碼，HTTP 以缺／錯／正確密碼切頁／亮度／上傳 | 缺／錯401，admin正確可修改；唯讀端點狀態依既有協定 | NOT RUN | |
| Q02 | 模擬忘密碼；USB握手後設定新密碼或清空，再測HTTP及重啟 | 不需舊密碼可實體復原；新設定保存；日誌不記明文密碼 | NOT RUN | |
| Q03 | 本機與另一 LAN 主機各 POST bridge /event | loopback合法事件成功，LAN拒絕；原有狀態讀取不受影響 | NOT RUN | |
| L01 | USB-only 與 WiFi 各連跑至少24小時，每小時切頁／換歌；記錄 uptime、重啟、heap（若可取得） | 無非預期重啟／死機／持续記憶體下滑；圖片傳輸不讓狀態長期停更 | NOT RUN | |
| L02 | 電腦睡眠／喚醒，USB hub 重插，網路 DHCP 變更 | 能自動或依明確操作恢復；記錄恢復時間及是否需手動重新配對 | NOT RUN | |

## 每個失敗另填一筆

| 欄位 | 填寫 |
|---|---|
| Case ID／日期／平台／commit | |
| 前置條件與精確重現步驟 | |
| 預期／實際行為 | |
| 重現次數（例如 3/5）／首次發生時間 | |
| 截圖、短影片、已去敏感資訊日誌路徑／issue URL | |
| 是否阻擋交付／暫時解法 | |
| 修復 commit／重測日期／重測結果 | |

驗收結束：統計 PASS／FAIL／BLOCKED／N/A；列出未解決問題、未有硬體的平台及測試限制。所有必要項目 PASS，或明確接受未解決限制後，才可以宣稱對應硬體已驗證。
