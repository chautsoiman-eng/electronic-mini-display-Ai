# TODO

## USB 有线直连（本轮做了 Mac + 固件，遗留项）
- ~~Windows 桥接串口支持~~（2026-10-09 已完成，待实机验证：`SerialLink.cs`，CH340/CP210x 自动扫描，推送 #STATUS/#NET/#PC/#WEATHER/#TIME）
- ~~有线模式下的控制通道（Windows）~~：屏幕切换、亮度、水平镜像在 USB 已握手时优先走 `#CMD`；Mac 端仍只走 HTTP
- 有线模式下音乐页数据（封面/文字条是二进制位图，需分帧或 base64，暂不支持，AUTO 不会自动切音乐页）
- 镜像弹窗在设备无 WiFi 时拉不到精灵图（GET /sprite/*/raw 走 HTTP），显示占位
