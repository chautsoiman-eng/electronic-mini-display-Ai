using System.Drawing;

namespace AIClockBridge;

static class PcMonitorScene
{
    internal static readonly Color Cyan = Color.FromArgb(88, 220, 222);
    static readonly Color Muted = Color.FromArgb(113, 151, 164);
    internal static string Pct(double? v) => PcTelemetry.Percent(v) is double n ? $"{n:0}%" : "--";
    internal static string Temp(double? v) => PcTelemetry.Temperature(v) is double n ? $"{n:0}C" : "--";

    public static void Draw(Graphics g, PcTelemetry data, bool preview = false)
    {
        using var font = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 10, FontStyle.Regular, GraphicsUnit.Pixel);
        using var right = new StringFormat { Alignment = StringAlignment.Far };
        using var grid = new Pen(Color.FromArgb(24, 71, 82));
        void Text(string text, int x, int y, Color color, bool tiny = false)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(text, tiny ? small : font, brush, x, y);
        }
        void Row(string title, int y, double? pct)
        {
            Text(title, 14, y, Color.White);
            g.DrawString(Pct(pct), font, Brushes.White, new RectangleF(160, y, 66, 18), right);
            using var track = new SolidBrush(Color.FromArgb(21, 48, 57));
            g.FillRectangle(track, 14, y + 18, 212, 6);
            if (PcTelemetry.Percent(pct) is double value)
            {
                using var fill = new SolidBrush(value >= 90 ? Color.OrangeRed : Cyan);
                g.FillRectangle(fill, 14, y + 18, (int)(212 * value / 100), 6);
            }
        }
        // 逾期資料不顯示為即時讀數；保留歷史並標示 STALE。
        Text(data.Stale ? "PC / DATA STALE" : preview ? "PC / LIVE PREVIEW" : "PC / MONITOR", 14, 8,
            data.Stale ? Color.OrangeRed : Cyan);
        g.DrawLine(grid, 14, 29, 226, 29);
        Row("CPU", 38, data.Stale ? null : data.CpuPct);
        Row("GPU MAX", 77, data.Stale ? null : data.GpuPct);
        Row("RAM", 116, data.Stale ? null : data.MemPct);
        Text("TEMP MAX", 14, 149, Muted, true);
        Text("CPU " + Temp(data.Stale ? null : data.CpuTempC), 14, 164, Color.White, true);
        Text("GPU " + Temp(data.Stale ? null : data.GpuTempC), 128, 164, Color.White, true);
        for (int y = 187; y <= 221; y += 17) g.DrawLine(grid, 14, y, 226, y);
        using var curve = new Pen(Cyan);
        var history = data.CpuHistory.TakeLast(60).ToArray();
        // 固定 60 秒寬度，未知點斷線；短歷史靠右對齊。
        for (int i = 1; i < history.Length; i++)
        {
            if (PcTelemetry.Percent(history[i - 1]) is not double a || PcTelemetry.Percent(history[i]) is not double b) continue;
            float x = 14 + (60 - history.Length + i) * 212f / 59;
            g.DrawLine(curve, x - 212f / 59, 221 - (float)a * .34f, x, 221 - (float)b * .34f);
        }
        Text("CPU HISTORY / 60s", 14, 227, Muted, true);
    }
}
