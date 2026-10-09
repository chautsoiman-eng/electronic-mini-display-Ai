using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AIClockBridge;

enum WeatherIconKind { None, Clear, PartlyCloudy, Cloudy, Fog, Rain, Showers, Snow, Thunderstorm }

static class WeatherScene
{
    internal static WeatherIconKind IconForCode(int? code) => code switch
    {
        0 => WeatherIconKind.Clear,
        1 or 2 => WeatherIconKind.PartlyCloudy,
        3 => WeatherIconKind.Cloudy,
        45 or 48 => WeatherIconKind.Fog,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 => WeatherIconKind.Rain,
        80 or 81 or 82 => WeatherIconKind.Showers,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherIconKind.Snow,
        95 or 96 or 99 => WeatherIconKind.Thunderstorm,
        _ => WeatherIconKind.None,
    };

    internal static void DrawIcon(Graphics g, WeatherIconKind kind, float cx, float cy)
    {
        if (kind == WeatherIconKind.None) return;
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var cyan = new Pen(ClockScene.Cyan, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var white = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var yellow = new Pen(Color.FromArgb(255, 204, 0), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var cloudFill = new SolidBrush(Color.FromArgb(192, 220, 226));
        using var yellowFill = new SolidBrush(Color.FromArgb(255, 204, 0));

        void Sun(float x, float y, float r)
        {
            g.FillEllipse(yellowFill, x - r, y - r, r * 2, r * 2);
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                g.DrawLine(yellow, x + (float)Math.Cos(a) * (r + 5), y + (float)Math.Sin(a) * (r + 5),
                    x + (float)Math.Cos(a) * (r + 11), y + (float)Math.Sin(a) * (r + 11));
            }
        }
        void Cloud(float x, float y)
        {
            g.FillEllipse(cloudFill, x - 20, y - 6, 24, 22);
            g.FillEllipse(cloudFill, x - 7, y - 15, 29, 31);
            g.FillEllipse(cloudFill, x + 12, y - 4, 22, 20);
            g.FillRectangle(cloudFill, x - 20, y + 3, 54, 14);
        }

        switch (kind)
        {
            case WeatherIconKind.Clear:
                Sun(cx, cy, 12);
                break;
            case WeatherIconKind.PartlyCloudy:
                Sun(cx - 10, cy - 9, 9);
                Cloud(cx + 2, cy + 5);
                break;
            case WeatherIconKind.Cloudy:
                Cloud(cx, cy);
                break;
            case WeatherIconKind.Fog:
                for (var i = -1; i <= 1; i++) g.DrawLine(cyan, cx - 25, cy + i * 10, cx + 25, cy + i * 10);
                break;
            default:
                Cloud(cx, cy - 8);
                if (kind is WeatherIconKind.Rain or WeatherIconKind.Showers)
                    for (var i = -1; i <= 1; i++) g.DrawLine(cyan, cx + i * 15, cy + 16, cx + i * 15 - 5, cy + 27);
                else if (kind == WeatherIconKind.Snow)
                    for (var i = -1; i <= 1; i++)
                    {
                        g.DrawLine(white, cx + i * 15 - 4, cy + 22, cx + i * 15 + 4, cy + 22);
                        g.DrawLine(white, cx + i * 15, cy + 18, cx + i * 15, cy + 26);
                    }
                else if (kind == WeatherIconKind.Thunderstorm)
                    g.DrawLines(yellow, new[] { new PointF(cx + 2, cy + 12), new PointF(cx - 5, cy + 25),
                        new PointF(cx + 3, cy + 25), new PointF(cx - 3, cy + 36) });
                break;
        }
        g.SmoothingMode = oldSmoothing;
    }

    public static void Draw(Graphics g, WeatherSnapshot data, bool testData = false)
    {
        data ??= new();
        using var title = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var temp = new Font("Consolas", 58, FontStyle.Bold, GraphicsUnit.Pixel);
        using var condition = new Font("Consolas", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using var row = new Font("Consolas", 14, FontStyle.Regular, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var grid = new Pen(ClockScene.Grid);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        g.Clear(Color.Black);

        void Text(string value, Font font, Color color, RectangleF bounds, StringFormat format = null)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(value, font, brush, bounds, format ?? StringFormat.GenericDefault);
        }

        var location = string.IsNullOrWhiteSpace(data.Location) ? "TAIPEI" : data.Location;
        if (location.Length > 14) location = location[..14];
        Text("WEATHER", title, ClockScene.Cyan, new RectangleF(14, 8, 100, 18));
        Text(testData ? "TEST DATA" : location, title, ClockScene.Muted,
            new RectangleF(116, 8, 110, 18), right);
        g.DrawLine(grid, 14, 29, 226, 29);

        if (data.IsLive) DrawIcon(g, IconForCode(data.WeatherCode), 58, 78);
        Text(data.IsLive ? ClockScene.TemperatureText(data) : "--", temp, Color.White,
            new RectangleF(data.IsLive ? 91 : 8, 46, data.IsLive ? 139 : 224, 66), center);
        Text(data.IsLive ? ClockScene.ConditionText(data).Replace("WEATHER ", "") : "WAITING FOR DATA",
            condition, data.IsLive ? ClockScene.Cyan : Color.OrangeRed,
            new RectangleF(8, 115, 224, 30), center);
        g.DrawLine(grid, 14, 155, 226, 155);

        Text("FEELS LIKE", row, ClockScene.Muted, new RectangleF(14, 169, 120, 20));
        Text(data.IsLive && data.ApparentC is double a ? $"{a:0}C" : "--", row, Color.White,
            new RectangleF(140, 169, 86, 20), right);
        Text("HUMIDITY", row, ClockScene.Muted, new RectangleF(14, 194, 120, 20));
        Text(data.IsLive && data.HumidityPct is int h ? $"{h}%" : "--", row, Color.White,
            new RectangleF(140, 194, 86, 20), right);
        Text("WIND", row, ClockScene.Muted, new RectangleF(14, 219, 120, 18));
        Text(data.IsLive && data.WindKph is double w ? $"{w:0} KM/H" : "--", row, Color.White,
            new RectangleF(126, 219, 100, 18), right);
    }
}
