#pragma once

// ---- Firmware version (shown on the first-time WiFi setup screen & /api/info) ----
#define FW_VERSION "0.5.0"

// ---- Clock / NTP ----
// POSIX TZ string; override at build time for another location.
// Asia/Taipei is UTC+8 year-round, represented by POSIX as CST-8.
#ifndef CLOCK_TIMEZONE
#define CLOCK_TIMEZONE "CST-8"
#endif
#ifndef CLOCK_TIMEZONE_LABEL
#define CLOCK_TIMEZONE_LABEL "TAIPEI"
#endif
#define CLOCK_NTP_SERVER_1 "pool.ntp.org"
#define CLOCK_NTP_SERVER_2 "time.google.com"

// ---- Bridge polling ----
#define BRIDGE_DEFAULT_PORT 8765
#define BRIDGE_DEFAULT_PATH "/status"
#define BRIDGE_POLL_INTERVAL_MS 5000
#define BRIDGE_HTTP_TIMEOUT_MS 3000

// ---- WiFiManager ----
#define WIFI_PORTAL_AP_NAME "AI-Clock-Setup"
#define WIFI_CONFIG_FILE "/bridge_host.txt"

// ---- Backlight ----
#define BRIGHTNESS_FILE "/brightness.txt"
#define BRIGHTNESS_DEFAULT 100
#define BRIGHTNESS_PWM_FREQ 2000 // Hz; high enough to avoid visible flicker when dim

// ---- Display layout (240x240 ST7789) ----
#define SCREEN_W 240
#define SCREEN_H 240
