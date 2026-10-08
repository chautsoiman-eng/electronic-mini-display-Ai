using System.Drawing;

namespace AIClockBridge;

// 使用與韌體相同的欄位；缺少額度時保留未知，不借用 session 時間。
readonly record struct HoloProvider(string Status, bool NeedsInput,
    double? ShortPct, int? ShortReset, double? WeeklyPct, int? WeeklyReset);

static class HoloAiScene
{
    internal static readonly Color Cyan = Color.FromArgb(88, 220, 222);
    static readonly Color Muted = Color.FromArgb(113, 151, 164);
    static readonly Color Track = Color.FromArgb(21, 48, 57);

    internal static bool Known(double? pct) => pct.HasValue && double.IsFinite(pct.Value) && pct >= 0;
    internal static string Percent(double? pct) => Known(pct)
        ? $"{(int)(Math.Clamp(pct.Value, 0, 100) + 0.5)}%" : "--";
    internal static Color UsageColor(double? pct) => !Known(pct) ? Color.DarkGray
        : pct >= 90 ? Color.Red : pct >= 70 ? Color.Orange : Cyan;
    internal static string Reset(int? minutes) => !minutes.HasValue || minutes < 0 ? "--"
        : minutes >= 1440 ? $"{minutes / 1440}d {minutes % 1440 / 60}h"
        : minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";

    public static void Draw(Graphics g, HoloProvider claude, HoloProvider codex, bool connected)
    {
        using var font = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        using var line = new Pen(Color.FromArgb(24, 71, 82));
        using var right = new StringFormat { Alignment = StringAlignment.Far };

        void Text(string text, float x, float y, Color color, bool tiny = false)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(text, tiny ? small : font, brush, x, y);
        }
        void Right(string text, float y, Color color)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, new RectangleF(100, y, 126, 18), right);
        }
        void Bar(int y, string label, double? pct, int? reset, bool compact = false)
        {
            Text(label, 14, y, Muted);
            Right(Percent(pct), y, Color.White);
            using var track = new SolidBrush(Track);
            int barY = compact ? 226 : y + 18;
            int height = compact ? 5 : 6;
            g.FillRectangle(track, 14, barY, 212, height);
            if (Known(pct))
            {
                using var fill = new SolidBrush(UsageColor(pct));
                g.FillRectangle(fill, 14, barY, (int)(212 * Math.Clamp(pct.Value, 0, 100) / 100), height);
            }
            if (!compact) Text("RESET " + Reset(reset), 14, y + 27, Muted, true);
        }

        // 依韌體的 240×240 座標繪製；離線狀態明確顯示，避免誤認為即時鏡像。
        Text(connected ? "HOLO / AI MONITOR" : "HOLO / DEVICE OFFLINE", 14, 8, connected ? Cyan : Color.OrangeRed);
        g.DrawLine(line, 14, 29, 226, 29);
        Text("CLAUDE", 14, 36, Color.White);
        Right(claude.NeedsInput ? "INPUT" : claude.Status ?? "unknown", 36, Cyan);
        Bar(57, "5H", claude.ShortPct, claude.ShortReset);
        Bar(99, "7D", claude.WeeklyPct, claude.WeeklyReset);
        g.DrawLine(line, 14, 138, 226, 138);
        Text("CODEX", 14, 145, Color.White);
        Right(codex.NeedsInput ? "INPUT" : codex.Status ?? "unknown", 145, Cyan);
        Bar(165, "5H", codex.ShortPct, codex.ShortReset);
        Bar(207, "7D", codex.WeeklyPct, codex.WeeklyReset, true);
    }
}
