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

    // Same layout constants as firmware drawHoloAi().
    internal const int PetBox = 60, PetX = 14, ClaudePetY = 40, CodexPetY = 146;
    internal const int BarX = 84, BarW = 142;

    /// Half-size pet rectangle for a w x h sprite, centred in its 60x60 box
    /// (firmware drawHoloPet samples every second row/column).
    internal static Rectangle PetRect(int boxY, int w, int h)
    {
        int dw = (w + 1) / 2, dh = (h + 1) / 2;
        return new Rectangle(PetX + (PetBox - dw) / 2, boxY + (PetBox - dh) / 2, dw, dh);
    }

    public static void Draw(Graphics g, HoloProvider claude, HoloProvider codex, bool connected,
                            Bitmap claudePet = null, Bitmap codexPet = null)
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
            g.DrawString(text, font, brush, new RectangleF(BarX + 40, y, 226 - BarX - 40, 18), right);
        }
        void Bar(int y, string label, double? pct, int? reset, bool compact = false)
        {
            Text(label, BarX, y, Muted);
            Right(Percent(pct), y, Color.White);
            using var track = new SolidBrush(Track);
            int barY = y + 18;
            int height = compact ? 5 : 6;
            g.FillRectangle(track, BarX, barY, BarW, height);
            if (Known(pct))
            {
                using var fill = new SolidBrush(UsageColor(pct));
                g.FillRectangle(fill, BarX, barY, (int)(BarW * Math.Clamp(pct.Value, 0, 100) / 100), height);
            }
            if (!compact) Text("RESET " + Reset(reset), BarX, y + 27, Muted, true);
        }
        void Pet(Bitmap pet, int boxY)
        {
            if (pet == null) return;
            var state = g.Save();
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(pet, PetRect(boxY, pet.Width, pet.Height));
            g.Restore(state);
        }
        void Header(string name, HoloProvider p, int y)
        {
            Text(name, BarX, y, Color.White);
            Right(p.NeedsInput ? "INPUT" : p.Status ?? "unknown", y, p.NeedsInput ? Color.Red : Cyan);
        }

        // 依韌體的 240×240 座標繪製；離線狀態明確顯示，避免誤認為即時鏡像。
        Text(connected ? "HOLO / AI MONITOR" : "HOLO / DEVICE OFFLINE", 14, 8, connected ? Cyan : Color.OrangeRed);
        g.DrawLine(line, 14, 29, 226, 29);
        Pet(claudePet, ClaudePetY);
        Header("CLAUDE", claude, 36);
        Bar(57, "5H", claude.ShortPct, claude.ShortReset);
        Bar(96, "7D", claude.WeeklyPct, claude.WeeklyReset);
        g.DrawLine(line, 14, 136, 226, 136);
        Pet(codexPet, CodexPetY);
        Header("CODEX", codex, 142);
        Bar(162, "5H", codex.ShortPct, codex.ShortReset);
        Bar(201, "7D", codex.WeeklyPct, codex.WeeklyReset, true);
    }
}
