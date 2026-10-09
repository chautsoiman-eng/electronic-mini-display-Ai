using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace AIClockBridge;

// 天氣先保留明確介面；沒有真實來源時維持 null，畫面顯示 --。
record WeatherSnapshot(double? TemperatureC = null, string Condition = null)
{
    public double? ApparentC { get; init; }
    public int? HumidityPct { get; init; }
    public double? WindKph { get; init; }
    public int? WeatherCode { get; init; }
    public string Location { get; init; } = "TAIPEI";
    public DateTimeOffset? UpdatedAt { get; init; }
    public bool IsLive { get; init; }
}

interface IClockWeatherSource
{
    WeatherSnapshot Snapshot { get; }
}

sealed class NullClockWeatherSource : IClockWeatherSource
{
    public WeatherSnapshot Snapshot { get; } = new();
}

record ClockSnapshot(bool Synchronized, DateTime LocalTime, string TimeZoneLabel,
                     WeatherSnapshot Weather)
{
    public long MinuteKey => Synchronized ? LocalTime.Ticks / TimeSpan.TicksPerMinute : -1;
}

static class ClockScene
{
    internal static readonly Color Cyan = Color.FromArgb(88, 220, 222);
    internal static readonly Color Muted = Color.FromArgb(113, 151, 164);
    internal static readonly Color Grid = Color.FromArgb(24, 71, 82);
    internal const string DefaultTimeZoneLabel = "TAIPEI";

    static readonly string[] Weekdays =
        { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    public static TimeZoneInfo TaipeiTimeZone()
    {
        foreach (var id in new[] { "Taipei Standard Time", "Asia/Taipei" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Taipei", TimeSpan.FromHours(8),
            "Asia/Taipei", "Asia/Taipei");
    }

    public static ClockSnapshot FromUtc(DateTimeOffset utc, WeatherSnapshot weather = null,
                                         TimeZoneInfo zone = null, string label = DefaultTimeZoneLabel)
    {
        zone ??= TaipeiTimeZone();
        var local = TimeZoneInfo.ConvertTime(utc, zone).DateTime;
        return new ClockSnapshot(true, local, label, weather ?? new());
    }

    internal static string TimeText(ClockSnapshot data) =>
        data.Synchronized ? data.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture) : "--:--";

    internal static string DateText(ClockSnapshot data) =>
        data.Synchronized ? data.LocalTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "---- -- --";

    internal static string WeekdayText(ClockSnapshot data) =>
        data.Synchronized ? Weekdays[(int)data.LocalTime.DayOfWeek] : "WAITING FOR NTP";

    internal static string TemperatureText(WeatherSnapshot weather) =>
        weather?.TemperatureC is double value && double.IsFinite(value) && value >= -90 && value <= 70
            ? $"{value:0}C" : "--";

    internal static string ConditionText(WeatherSnapshot weather)
    {
        var value = weather?.Condition?.Trim();
        if (string.IsNullOrEmpty(value)) return "WEATHER --";
        value = value.ToUpperInvariant();
        return value.Length <= 14 ? value : value[..14];
    }

    // 與 ESP8266 drawClockScreen 使用相同的原生 240×240 座標。
    public static void Draw(Graphics g, ClockSnapshot data, bool testData = false)
    {
        using var regular = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var clock = new Font("Consolas", 50, FontStyle.Bold, GraphicsUnit.Pixel);
        using var date = new Font("Consolas", 19, FontStyle.Regular, GraphicsUnit.Pixel);
        using var weather = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var grid = new Pen(Grid);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        g.Clear(Color.Black);

        void Text(string value, Font font, Color color, RectangleF bounds, StringFormat format = null)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(value, font, brush, bounds, format ?? StringFormat.GenericDefault);
        }

        Text("CLOCK", regular, Cyan, new RectangleF(14, 8, 100, 18));
        var zoneLabel = string.IsNullOrWhiteSpace(data.TimeZoneLabel) ? "UTC" : data.TimeZoneLabel.Trim();
        if (zoneLabel.Length > 14) zoneLabel = zoneLabel[..14];
        Text(testData ? "TEST DATA" : zoneLabel, small, Muted,
            new RectangleF(116, 9, 110, 16), right);
        g.DrawLine(grid, 14, 29, 226, 29);

        Text(TimeText(data), clock, Color.White, new RectangleF(10, 43, 220, 60), center);
        Text(DateText(data), date, Color.White, new RectangleF(10, 112, 220, 28), center);
        Text(WeekdayText(data), regular, data.Synchronized ? Cyan : Color.OrangeRed,
            new RectangleF(10, 143, 220, 22), center);

        g.DrawLine(grid, 14, 178, 226, 178);
        Text("WEATHER", small, Muted, new RectangleF(14, 187, 90, 16));
        Text(TemperatureText(data.Weather), weather, Color.White, new RectangleF(14, 201, 90, 34));
        Text(ConditionText(data.Weather), regular, Cyan, new RectangleF(103, 205, 123, 22), right);
    }
}
