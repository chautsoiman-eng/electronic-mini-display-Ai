# Electronic Mini Display · Holo Dashboard

ESP8266 / SD2 with a **1.54-inch ST7789, 240×240** display and a Windows or macOS bridge. Work stays on **`feature/holo-dashboard`**; do not modify or merge `main`. Hardware has **not** been received or validated. Build/test success is not physical acceptance. [中文](README.md) · [Verification](docs/HOLO_VERIFICATION.md) · [Hardware checklist](docs/HARDWARE_ACCEPTANCE.md)

## Screens and platform support

| Firmware screen | Contents | Data source / limitation |
|---|---|---|
| AI Monitor (`holo_ai`) | Claude/Codex 5-hour and 7-day usage, reset countdowns, activity, pets | Local CLI credentials and sessions; unavailable quota stays `--` |
| PC Monitor (`pc`) | CPU/GPU load and temperature, RAM, CPU history | Windows bridge; unavailable sensors stay unknown |
| Clock (`clock`) | Time, weekday, date, weather footer | WiFi NTP or USB `#TIME`; NTP takes priority once available; firmware currently uses Taipei timezone |
| Weather (`weather`) | Temperature, feels-like, humidity, wind, conditions | Windows Open-Meteo source, Taipei by default; computer needs internet |
| Music (`music`) | Artwork, rendered title/artist, progress, 24 spectrum bars | Windows has WASAPI FFT; Mac has metadata/artwork but **no FFT implementation** |

Windows provides the five page controls/previews. Mac currently previews the original pets, network and music pages; it has no Holo/PC/Clock/Weather preview or PC/weather provider. The firmware web page can select all five modes. Desktop mirrors redraw data; they are not device screenshots. GIF upload/reset still requires HTTP/WiFi.

## Build and install this branch

Use this branch's source or successful [GitHub Actions artifacts](https://github.com/chautsoiman-eng/electronic-mini-display-Ai/actions/workflows/holo-build.yml), not an upstream web flasher/release that may lack these changes.

```sh
git clone --single-branch --branch feature/holo-dashboard https://github.com/chautsoiman-eng/electronic-mini-display-Ai.git
cd electronic-mini-display-Ai
python3 -m venv firmware/.pio-venv
firmware/.pio-venv/bin/python -m pip install platformio==6.1.19
firmware/.pio-venv/bin/python -m platformio run -d firmware -e nodemcuv2
```

On Windows use `py -m venv` and `firmware/.pio-venv/Scripts/python.exe`. Stop the bridge and serial monitors before flashing. With that environment's Python, run `python -m serial.tools.list_ports -v`, then `python -m platformio run -d firmware -e nodemcuv2 -t upload --upload-port PORT`, replacing PORT with your actual port. Do not reuse the author's fixed port in platformio.ini.

Use a USB **data** cable. CH340/CH341 and CP210x may require a suitable vendor driver. Mac candidates include `/dev/cu.usbserial*`, `/dev/cu.wchusbserial*`, `/dev/cu.SLAB_USBtoUART*`; Windows uses `COMx`. Check your board's voltage and wiring against [platformio.ini](firmware/platformio.ini) and [development notes](docs/DEVELOPMENT.md) before powering bare modules.

**macOS 12+, Swift 5.9+ / Command Line Tools:**

```sh
swift build --package-path mac-app -c release
swift test --package-path mac-app
mac-app/.build/release/AIClockBridge
```

Full Xcode is needed if the installed Command Line Tools do not include XCTest. Allow local-network access when prompted. Right-click the menu-bar icon for controls/password; left-click for mirror/brightness. Music uses private MediaRemote APIs: support varies by macOS and player and requires testing.

**Windows / .NET 8 SDK:**

```powershell
dotnet build windows-app/AIClockBridge/AIClockBridge.csproj -c Release
dotnet publish windows-app/AIClockBridge/AIClockBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
```

Run `dist/windows/AIClockBridge.exe`, or download and extract the branch CI's `windows-portable` artifact. The self-contained build needs no separately installed .NET runtime. Sensor availability depends on hardware/permissions.

## Connections and mirroring

- **WiFi:** join `AI-Clock-Setup`, open `192.168.4.1`, configure 2.4 GHz WiFi. Device and computer must be reachable. Bridge port is `8765`; enter the device IP and computer `IP:8765` manually if automatic pairing fails.
- **USB-only:** no device IP or WiFi setup is needed. The bridge scans and handshakes automatically. Mac sends status, network, time and music; Windows additionally sends PC/weather and FFT. Internet on the computer is still needed for fresh remote quota/weather.
- **Artwork/text:** device `#NEED` requests trigger RGB565 `#IMG` row frames. At 115200 baud, images arrive progressively. Mac starts at most one frame per 125 ms, retains partial writes, and drops queued obsolete revisions.
- **Sprites:** `#INFO` supplies device state; `#CMD sprite_dump` requests `#SPR` chunks. Cache keys include connection, slot, revision and dimensions. Allow roughly 25–40 seconds for initial loading, with a 60-second deadline. Disconnect clears incomplete transfers/cache. USB download is not USB GIF upload.
- **Recovery:** Mac reconnects after 12 seconds without device replies. Firmware leaves wired-active about 15 seconds after the last serial frame. Test WiFi fallback with independent safe power; unplugging USB power is a reboot, not seamless failover.
- **45° optics:** use Windows controls, Mac's horizontal-mirror menu item, or the device web page; USB command is `#CMD {"mirror":true}`. It persists on the device. Verify asymmetric text, arrows, full-image orientation, viewing angle and clipping physically.

## Password and security

The optional device admin password defaults to empty. HTTP mutations use Basic Auth with username `admin`. HTTP has **no TLS**; Basic Auth is not encryption. Use a trusted LAN, never expose the device or port 8765 publicly. Read-only status can still disclose usage information. Mac stores the password in UserDefaults and firmware stores it in LittleFS, not secure credential stores.

To recover a forgotten password, handshake over USB and use the bridge password menu to replace/clear it. Alternatively, stop the bridge and send newline-terminated `#HELLO`, then `#CMD {"admin_password":""}` at 115200 baud. USB physical access does not require the old password. Verify persistence and HTTP behavior afterward. Bridge `/event` accepts loopback only. Never commit tokens, login files, passwords or sensitive logs.

See [verification commands](docs/HOLO_VERIFICATION.md), [TODO](TODO.md), and the executable [hardware acceptance checklist](docs/HARDWARE_ACCEPTANCE.md), which includes expected results and failure records. Every hardware item currently remains NOT RUN.
