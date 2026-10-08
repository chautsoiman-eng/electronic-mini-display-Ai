# 電子小螢幕｜Holo Dashboard

This branch develops a 240×240 ST7789 HoloCubic-style interface for the SD2 ESP8266 WiFi clock, while preserving upstream features.

## Confirmed screens

1. **AI Monitor**: Claude and Codex, each with 5-hour and 7-day usage bars, percentages, reset countdowns, and working/idle state. Missing quota is displayed as `--`, never 0%.
2. **PC Monitor**: CPU/GPU load and temperatures, RAM usage bar, and CPU load history.
3. **Weather**: temperature, conditions, feels-like temperature, humidity, and wind.
4. **Clock**: large time, weekday, date, and simple weather.
5. **Audio Visualizer**: 24 logarithmic spectrum bars, bass on left, treble on right, fast attack/slow decay and peak hold, fed by Windows WASAPI loopback/FFT.

## Visual and compatibility requirements

- Black background, cyan/teal thin-line futuristic HUD; 240×240 native resolution.
- Optional horizontal mirroring for 45-degree beam-splitter reflection.
- Preserve WiFiManager, LittleFS custom GIF sprites, original device HTTP API, USB serial fallback, and Windows bridge's actual Claude/Codex OAuth usage polling.
- Keep auto-switching to music on playback; avoid falsely claiming real FFT until Windows capture and ESP rendering are implemented.
- Develop on `feature/holo-dashboard`, not `main`.

## Development status

- [x] Fork verified and feature branch created.
- [x] UI requirements agreed.
- [x] AI Monitor firmware integrated and PlatformIO build verified (e3f6e4a, nodemcuv2).
- [x] Windows Holo AI menu, mirror preview, and render regression checks.
- [ ] AI Monitor physical-device validation (USB device not connected on the development PC).
- [ ] PC telemetry and monitor screen.
- [ ] Weather bridge and screen.
- [ ] Clock and time sync.
- [ ] WASAPI spectrum transport and display.
- [ ] Horizontal mirror rendering and physical-device validation.

Build and device-validation instructions: [Holo verification](docs/HOLO_VERIFICATION.md).
