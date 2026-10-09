using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AIClockBridge;

// Native 240x240 coordinates; shared with drawPcScreen() in firmware.
static class PcMonitorScene
{
    internal static readonly Color Cyan = Color.FromArgb(88, 220, 222);
    static readonly Color Muted = Color.FromArgb(113, 151, 164);
    static readonly Color Grid = Color.FromArgb(24, 71, 82);
    static readonly Color Track = Color.FromArgb(21, 48, 57);
    internal static string Pct(double? v) => PcTelemetry.Percent(v) is double n ? $"{n:0}%" : "--";
    internal static string Temp(double? v) => PcTelemetry.Temperature(v) is double n ? $"{n:0}C" : "--";

    public static void Draw(Graphics g, PcTelemetry data, bool preview = false)
    {
        using var font = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var big = new Font("Consolas", 27, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var grid = new Pen(Grid);
        using var curve = new Pen(Cyan, 1.5f);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        void Text(string s, int x, int y, Color c, Font f)
        {
            using var b = new SolidBrush(c);
            g.DrawString(s, f, b, x, y);
        }
        void Right(string s, int y, Color c)
        {
            using var b = new SolidBrush(c);
            g.DrawString(s, font, b, new RectangleF(126, y, 100, 22), right);
        }
        void Bar(int x, int y, int w, double? value)
        {
            using var track = new SolidBrush(Track);
            g.FillRectangle(track, x, y, w, 6);
            if (PcTelemetry.Percent(value) is double pct && pct > 0)
            {
                using var fill = new SolidBrush(pct >= 90 ? Color.OrangeRed : Cyan);
                g.FillRectangle(fill, x, y, (int)(w * pct / 100), 6);
            }
        }

        bool stale = data.Stale;
        Text("PC MONITOR", 14, 8, Cyan, font);
        Right(stale ? "STALE" : "LIVE", 8, stale ? Color.OrangeRed : Cyan);
        g.DrawLine(grid, 14, 29, 226, 29);

        Text("CPU", 14, 37, Muted, font);
        Text("GPU MAX", 126, 37, Muted, font);
        Text(Pct(stale ? null : data.CpuPct), 14, 55, Color.White, big);
        Text(Pct(stale ? null : data.GpuPct), 126, 55, Color.White, big);
        Text("TEMP " + Temp(stale ? null : data.CpuTempC), 14, 87, Muted, small);
        Text("TEMP " + Temp(stale ? null : data.GpuTempC), 126, 87, Muted, small);
        Bar(14, 103, 98, stale ? null : data.CpuPct);
        Bar(126, 103, 100, stale ? null : data.GpuPct);

        Text("RAM", 14, 119, Muted, font);
        Right(Pct(stale ? null : data.MemPct), 119, Color.White);
        Bar(14, 141, 212, stale ? null : data.MemPct);

        g.DrawLine(grid, 14, 158, 226, 158);
        Text("CPU HISTORY", 14, 164, Muted, font);
        Right("60s", 164, Muted);
        foreach (int y in new[] { 188, 207, 226 }) g.DrawLine(grid, 14, y, 226, y);

        // Right-aligned 60 one-second slots; unknown samples break the line.
        var history = data.CpuHistory.TakeLast(60).ToArray();
        for (int i = 1; i < history.Length; i++)
        {
            if (PcTelemetry.Percent(history[i - 1]) is not double a ||
                PcTelemetry.Percent(history[i]) is not double b) continue;
            float x0 = 14 + (60 - history.Length + i - 1) * 212f / 59;
            float x1 = 14 + (60 - history.Length + i) * 212f / 59;
            g.DrawLine(curve, x0, 226 - (float)a * .38f, x1, 226 - (float)b * .38f);
        }
    }
}
