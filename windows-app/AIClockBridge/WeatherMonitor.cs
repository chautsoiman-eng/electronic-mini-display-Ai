using System.Globalization;
using System.Text.Json;

namespace AIClockBridge;

// 從免金鑰的 Open-Meteo 取得即時天氣。網路失敗時保留最後資料，
// 超過 30 分鐘便標示為非即時，畫面不會用測試值冒充真實天氣。
sealed class WeatherMonitor : IClockWeatherSource, IDisposable
{
    const string DefaultLat = "25.0330";
    const string DefaultLon = "121.5654";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly object _gate = new();
    System.Threading.Timer _timer;
    WeatherSnapshot _snapshot = new();
    int _refreshing;

    public WeatherSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                if (_snapshot.UpdatedAt is { } at && DateTimeOffset.UtcNow - at > TimeSpan.FromMinutes(30))
                    return _snapshot with { IsLive = false };
                return _snapshot;
            }
        }
    }

    public void Start()
    {
        _ = Refresh();
        _timer = new System.Threading.Timer(_ => _ = Refresh(), null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public async Task Refresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            var lat = Coordinate("weather_latitude", DefaultLat, -90, 90);
            var lon = Coordinate("weather_longitude", DefaultLon, -180, 180);
            var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat
                + "&longitude=" + lon
                + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m"
                + "&timezone=auto&forecast_days=1";
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var parsed = Parse(json, Setting("weather_location", "TAIPEI"));
            lock (_gate) _snapshot = parsed;
        }
        catch
        {
            // 天氣是輔助資訊；失敗不可中斷 Clock 或橋接服務。
        }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    static string Coordinate(string key, string fallback, double min, double max)
    {
        var raw = Setting(key, fallback);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && value >= min && value <= max
            ? value.ToString("0.####", CultureInfo.InvariantCulture) : fallback;
    }

    internal static string Setting(string key, string fallback)
    {
        var value = Settings.Get(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    internal static WeatherSnapshot Parse(string json, string location)
    {
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement.GetProperty("current");
        var code = current.GetProperty("weather_code").GetInt32();
        return new WeatherSnapshot(current.GetProperty("temperature_2m").GetDouble(), Condition(code))
        {
            ApparentC = current.TryGetProperty("apparent_temperature", out var apparent) ? apparent.GetDouble() : null,
            HumidityPct = current.TryGetProperty("relative_humidity_2m", out var humidity) ? humidity.GetInt32() : null,
            WindKph = current.TryGetProperty("wind_speed_10m", out var wind) ? wind.GetDouble() : null,
            WeatherCode = code,
            Location = string.IsNullOrWhiteSpace(location) ? "TAIPEI" : location.Trim().ToUpperInvariant(),
            UpdatedAt = DateTimeOffset.UtcNow,
            IsLive = true,
        };
    }

    internal static string Condition(int code) => code switch
    {
        0 => "CLEAR",
        1 or 2 => "PARTLY CLOUDY",
        3 => "OVERCAST",
        45 or 48 => "FOG",
        51 or 53 or 55 or 56 or 57 => "DRIZZLE",
        61 or 63 or 65 or 66 or 67 => "RAIN",
        71 or 73 or 75 or 77 => "SNOW",
        80 or 81 or 82 => "SHOWERS",
        85 or 86 => "SNOW SHOWERS",
        95 or 96 or 99 => "THUNDERSTORM",
        _ => "UNKNOWN",
    };

    public byte[] ToJson()
    {
        var s = Snapshot;
        return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["valid"] = s.IsLive,
            ["temperature_c"] = s.IsLive ? s.TemperatureC : null,
            ["apparent_c"] = s.IsLive ? s.ApparentC : null,
            ["humidity_pct"] = s.IsLive ? s.HumidityPct : null,
            ["wind_kph"] = s.IsLive ? s.WindKph : null,
            ["weather_code"] = s.IsLive ? s.WeatherCode : null,
            ["condition"] = s.IsLive ? s.Condition : "",
            ["location"] = s.Location,
        });
    }

    public void Dispose() => _timer?.Dispose();
}
