using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AIClockBridge;

static class WeatherScene
{
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

        Text(data.IsLive ? ClockScene.TemperatureText(data) : "--", temp, Color.White,
            new RectangleF(8, 46, 224, 66), center);
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
